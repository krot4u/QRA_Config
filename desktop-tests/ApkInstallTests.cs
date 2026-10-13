using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using QraConfig.Services;

internal static class ApkInstallTests
{
    public static async Task Run()
    {
        void Check(bool success,string name) { if(!success) throw new Exception(name); Console.WriteLine("PASS "+name); }
        void Reject(Action action) { try { action(); } catch(InvalidOperationException) { return; } throw new Exception("Invalid input accepted"); }
        var uri = ApkInstallService.GetManifestUri(false);
        var manifest = new JsonObject { ["schemaVersion"]=1,["packageName"]=ApkInstallService.ProductionPackage,["channel"]="Global",["versionCode"]=279,["size"]=2000,["sha256"]=new string('a',64),["apkUrl"]=new Uri(uri,"279.apk").AbsoluteUri };
        Check(ApkInstallService.ParseManifest(manifest.ToJsonString(),uri,false).VersionCode==279,"APK public manifest accepted");
        foreach(var url in new[]{"http://s3.twcstorage.ru/test.apk","https://evil.example/test.apk",new Uri(uri,"../elsewhere.apk").AbsoluteUri,new Uri(uri,"279.apk?token=x").AbsoluteUri}) { var bad=(JsonObject)manifest.DeepClone();bad["apkUrl"]=url;Reject(()=>ApkInstallService.ParseManifest(bad.ToJsonString(),uri,false)); }
        Check(true,"APK manifest rejects insecure, foreign, outside-prefix and queried URLs");
        foreach(var key in new[]{"packageName","channel","sha256"}) { var bad=(JsonObject)manifest.DeepClone();bad[key]="invalid";Reject(()=>ApkInstallService.ParseManifest(bad.ToJsonString(),uri,false)); }
        var oversized=(JsonObject)manifest.DeepClone();oversized["size"]=ApkInstallService.MaximumApkSize+1;Reject(()=>ApkInstallService.ParseManifest(oversized.ToJsonString(),uri,false));
        Check(true,"APK manifest rejects package, channel, hash and excessive size");
        BackendService.ValidateInstallOutput("Performing Streamed Install\nSuccess");
        foreach(var output in new[]{"Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]","NotSuccess","Success\nFailure [error]"}) Reject(()=>BackendService.ValidateInstallOutput(output));
        Check(true,"APK install requires exact success and rejects mixed failure");
        var directory=Path.Combine(Path.GetTempPath(),"qra-apk-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try {
            foreach(var package in new[]{ApkInstallService.ProductionPackage,ApkInstallService.ReviewPackage}) { var path=Path.Combine(directory,package+".apk");Create(path,package);Check(await ApkInstallService.ValidateLocalAsync(path)==package,"APK binary manifest reads "+package); }
            var unrelated=Path.Combine(directory,"unrelated.apk");Create(unrelated,"com.other.app");try { await ApkInstallService.ValidateLocalAsync(unrelated);throw new Exception("Unrelated accepted"); } catch(InvalidOperationException) {}
            var fake=Path.Combine(directory,"fake.apk");File.WriteAllText(fake,"not a ZIP");try { await ApkInstallService.ValidateLocalAsync(fake);throw new Exception("Fake accepted"); } catch(InvalidOperationException) {}
            Check(true,"APK rejects unrelated package and malformed ZIP");
        } finally { Directory.Delete(directory,true); }
    }
    private static void Create(string path,string package)
    {
        var strings=new[]{"manifest","package",package};using var stringData=new MemoryStream();using var writer=new BinaryWriter(stringData,Encoding.UTF8,true);var offsets=new List<int>();
        foreach(var text in strings) { offsets.Add((int)stringData.Length);writer.Write((byte)text.Length);writer.Write((byte)Encoding.UTF8.GetByteCount(text));writer.Write(Encoding.UTF8.GetBytes(text));writer.Write((byte)0); }
        using var xml=new MemoryStream();using var b=new BinaryWriter(xml);int poolSize=28+12+(int)stringData.Length;b.Write((ushort)3);b.Write((ushort)8);b.Write(8+poolSize+56);b.Write((ushort)1);b.Write((ushort)28);b.Write(poolSize);b.Write(3);b.Write(0);b.Write(256);b.Write(40);b.Write(0);foreach(var offset in offsets)b.Write(offset);b.Write(stringData.ToArray());
        b.Write((ushort)0x102);b.Write((ushort)16);b.Write(56);b.Write(1);b.Write(-1);b.Write(-1);b.Write(0);b.Write((ushort)20);b.Write((ushort)20);b.Write((ushort)1);b.Write((ushort)0);b.Write((ushort)0);b.Write((ushort)0);b.Write(-1);b.Write(1);b.Write(2);b.Write((ushort)8);b.Write((byte)0);b.Write((byte)3);b.Write(2);
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);using(var output=zip.CreateEntry("AndroidManifest.xml").Open())output.Write(xml.ToArray());using(var output=zip.CreateEntry("classes.dex").Open())output.Write("dex\n"u8);
    }
}
