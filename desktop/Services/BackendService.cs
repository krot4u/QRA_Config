using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using QraConfig.Models;

namespace QraConfig.Services;

public sealed class BackendService
{
    private readonly string adbPath;
    private readonly string package;
    private readonly SemaphoreSlim gate = new(1,1);
    public BackendService(bool review = false, string? adbPath = null) { this.adbPath = adbPath ?? AdbBundle.Extract(); package = review ? "com.example.qra_frn.review" : "com.example.qra_frn"; }

    private async Task<string> RunAsync(IEnumerable<string> arguments, string? input, CancellationToken ct, bool install = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(install ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(120));
        using var process = new Process { StartInfo = new ProcessStartInfo(adbPath) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 } };
        foreach(var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start(); var output=process.StandardOutput.ReadToEndAsync(timeout.Token); var errors=process.StandardError.ReadToEndAsync(timeout.Token);
            if(input is not null) { var bytes=Encoding.UTF8.GetBytes(input); await process.StandardInput.BaseStream.WriteAsync(bytes,timeout.Token); }
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token); var value=await output; var error=await errors;
            if (install && (process.ExitCode != 0 || error.Contains("Failure",StringComparison.OrdinalIgnoreCase))) { var code=Regex.Match(value+"\n"+error,@"INSTALL_[A-Z_]+").Value; throw new InvalidOperationException("Android отклонил установку APK"+(code.Length>0?": "+code:"")+". Проверьте версию, подпись и разрешение установки. Приложение не удалялось."); }
            if(process.ExitCode!=0 || value.Contains("Exception",StringComparison.Ordinal) || value.StartsWith("Error",StringComparison.Ordinal)) throw new InvalidOperationException("ADB отклонил запрос. Проверьте подключение и согласие на телефоне.");
            return value.Trim();
        }
        catch(OperationCanceledException) { try { if(!process.HasExited) process.Kill(true); } catch {} throw; }
        catch(InvalidOperationException) { throw; }
        catch { throw new InvalidOperationException("Не удалось выполнить запрос ADB."); }
    }
    private async Task<T> Serialized<T>(Func<Task<T>> action,CancellationToken ct) { await gate.WaitAsync(ct); try { return await action(); } finally { gate.Release(); } }
    public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct=default) => Serialized(async () => {
        var rows=await RunAsync(["devices","-l"],null,ct); var devices=new List<DeviceInfo>();
        foreach(var line in rows.Split('\n')) {
            var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries); if(fields.Length<2 || fields[0]=="List" || fields[0].StartsWith('*') || fields[0].StartsWith("emulator-") || fields[0].Contains(':')) continue;
            if(fields[1]=="device" && !await IsUsbAsync(fields[0],ct)) continue;
            var model=fields.FirstOrDefault(x=>x.StartsWith("model:"))?[6..] ?? "Android"; devices.Add(new(fields[0],fields[1],model));
        }
        return (IReadOnlyList<DeviceInfo>)devices;
    },ct);
    public static bool UsbEvidenceMatches(string serial,string devPath,string? usbSerial,bool windows) => devPath.StartsWith("usb:",StringComparison.Ordinal) || windows && devPath=="unknown" && usbSerial==serial;
    private async Task<bool> IsUsbAsync(string serial,CancellationToken ct) {
        var path=await RunAsync(["-s",serial,"get-devpath"],null,ct); string? usb=null;
        if(OperatingSystem.IsWindows() && path=="unknown") { try { usb=await RunAsync(["-d","get-serialno"],null,ct); } catch(InvalidOperationException) {} }
        return UsbEvidenceMatches(serial,path,usb,OperatingSystem.IsWindows());
    }
    private async Task RequireUsb(string serial,CancellationToken ct) { if(string.IsNullOrWhiteSpace(serial) || serial.Contains(':') || !await IsUsbAsync(serial,ct)) throw new InvalidOperationException("Подключите выбранное устройство по USB."); }
    private static void CheckToken(string token) { if(!Regex.IsMatch(token,"^[a-zA-Z0-9_-]{16,128}$")) throw new InvalidOperationException("Сеанс настройки закрыт."); }
    private string Uri => "content://"+package+".config";
    private static SessionStatus ParseStatus(string value,string fallback="") {
        string Get(string name) { var m=Regex.Match(value,@"(?:\{|,|\[|\s)"+name+@"=([^,}\]]*)"); return m.Success?m.Groups[1].Value.Trim():""; }
        return new(Get("token") is {Length:>0} token?token:fallback,Get("status"),Get("operation"),Get("message"));
    }
    public Task<SessionStatus> BeginAsync(string serial,CancellationToken ct=default) => Serialized(async()=> {
        await RequireUsb(serial,ct); var status=ParseStatus(await RunAsync(["-s",serial,"shell","content","call","--uri",Uri,"--method","begin"],null,ct)); CheckToken(status.Token);
        try { await RunAsync(["-s",serial,"shell","am","start","-n",package+"/com.example.qra_frn.data.config.ConfigConsentActivity","--es","token",status.Token],null,ct); }
        catch { await RunAsync(["-s",serial,"shell","content","call","--uri",Uri,"--method","end","--arg",status.Token],null,CancellationToken.None); throw; }
        return status;
    },ct);
    public Task<SessionStatus> StatusAsync(string serial,string token,CancellationToken ct=default) => Serialized(async()=> { CheckToken(token); await RequireUsb(serial,ct); return ParseStatus(await RunAsync(["-s",serial,"shell","content","call","--uri",Uri,"--method","status","--arg",token],null,ct),token); },ct);
    internal static async Task<ConfigSnapshot> WaitForConsentAndReadAsync(
        Func<CancellationToken,Task<SessionStatus>> status,
        Func<CancellationToken,Task<ConfigSnapshot>> read,
        int attempts,
        TimeSpan pollDelay,
        CancellationToken ct)
    {
        if(attempts<=0) throw new ArgumentOutOfRangeException(nameof(attempts));
        for(var i=0;i<attempts;i++) {
            var state=await status(ct);
            if(state.Status=="closed") throw new InvalidOperationException("Доступ отклонён или сеанс завершён. Подключитесь заново.");
            if(state.Status=="ready") return await read(ct);
            if(i+1<attempts) await Task.Delay(pollDelay,ct);
        }
        throw new InvalidOperationException("Время ожидания подтверждения (3 минуты) истекло. Подключитесь заново.");
    }
    public Task<ConfigSnapshot> ReadAsync(string serial,string token,CancellationToken ct=default) => Serialized(async()=> {
        CheckToken(token); await RequireUsb(serial,ct); var raw=await RunAsync(["-s",serial,"shell","content","read","--uri",Uri+"/session/"+token],null,ct);
        try { var doc=JsonNode.Parse(raw)!.AsObject(); return new ConfigSnapshot(doc["baseline"]!.GetValue<string>(),doc["config"]!.AsObject(),doc["metadata"]?.AsObject()??new()); }
        catch { throw new InvalidOperationException("Телефон не вернул настройки. Подтвердите доступ и прочитайте заново."); }
    },ct);
    public Task ApplyAsync(string serial,string token,ConfigSnapshot original,ConfigSnapshot draft,IEnumerable<string> scopes,CancellationToken ct=default) => Serialized(async()=> {
        CheckToken(token); await RequireUsb(serial,ct); var selected=scopes.Distinct().ToArray(); var config=ConfigModel.MergeScopes(original.Config,draft.Config,selected);
        var payload=new JsonObject { ["baseline"]=original.Baseline,["scopes"]=new JsonArray(selected.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()),["config"]=config }.ToJsonString();
        if(Encoding.UTF8.GetByteCount(payload)>1024*1024) throw new InvalidOperationException("Настройки превышают допустимый размер.");
        await RunAsync(["-s",serial,"shell","content","write","--uri",Uri+"/session/"+token],payload,ct); return true;
    },ct);
    public static void ValidateInstallOutput(string output)
    {
        if (output.Split('\n').Any(line => line.Trim() == "Success") && !output.Contains("Failure", StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException("Android не подтвердил установку APK. Проверьте подпись, версию и разрешение установки на телефоне; приложение не удалялось.");
    }
    public Task InstallAsync(string serial,string apkPath,CancellationToken ct=default) => Serialized(async()=> {
        await RequireUsb(serial,ct);
        var apkPackage = await ApkInstallService.ValidateLocalAsync(apkPath,ct);
        if (apkPackage != package) throw new InvalidOperationException("APK предназначен для другой копии QRA-Talk. Выберите APK для текущего приложения.");
        var output = await RunAsync(["-s",serial,"install","-r",Path.GetFullPath(apkPath)],null,ct,install:true);
        ValidateInstallOutput(output);
        var installed = await RunAsync(["-s",serial,"shell","pm","path",package],null,ct);
        if (!installed.Split('\n').Any(line => line.StartsWith("package:",StringComparison.Ordinal))) throw new InvalidOperationException("После установки QRA-Talk не найден на устройстве.");
        return true;
    },ct);
    public Task EndAsync(string serial,string token,CancellationToken ct=default) => Serialized(async()=> { CheckToken(token); await RunAsync(["-s",serial,"shell","content","call","--uri",Uri,"--method","end","--arg",token],null,ct); return true; },ct);
}
