using System.Text.Json.Nodes;
using QraConfig.Models;
using QraConfig.Services;

static void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS "+name); }
JsonObject Configuration() => new() { ["format"]="qra-frn-settings",["configVersion"]=2,["settings"]=new JsonObject { ["appTheme"]="Dark",["userEmail"]="initial",["pttKeyCode"]=5,["frnServers"]="[]" } };
var original=Configuration(); var draft=(JsonObject)original.DeepClone(); var settings=draft["settings"]!.AsObject(); settings["appTheme"]="Light";settings["userEmail"]="changed";settings["pttKeyCode"]=99;
var merged=ConfigModel.MergeScopes(original,draft,["COMMON"])["settings"]!.AsObject();
Check(merged["appTheme"]!.GetValue<string>()=="Light" && merged["userEmail"]!.GetValue<string>()=="initial" && merged["pttKeyCode"]!.GetValue<int>()==5,"scope merge protects buttons and FRN");
Check(original["settings"]!["appTheme"]!.GetValue<string>()=="Dark","original immutable");
Check(ConfigModel.ScopeFor("pocAvatar:poc:host:443")=="POC" && ConfigModel.ScopeFor("profileAudio:p25:host:443")=="P25","profile metadata scopes");
Check(!BackendService.UsbEvidenceMatches("phone","unknown","other",true) && BackendService.UsbEvidenceMatches("phone","unknown","phone",true) && !BackendService.UsbEvidenceMatches("phone","unknown","phone",false),"USB unknown requires exact Windows physical selector");
var profiles=original["settings"]!.AsObject();ConfigModel.SaveProfile(profiles,new("PoC","example.org",443,"POC"));profiles["pocAvatar:poc:example.org:443"]="";profiles["server:poc:example.org:443:userPassword"]="private";
ConfigModel.SaveProfile(profiles,new("PoC moved","example.org",444,"POC"),"poc:example.org:443");
Check(profiles.ContainsKey("server:poc:example.org:444:userPassword") && !profiles.ContainsKey("server:poc:example.org:443:userPassword"),"profile move retains private fields");ConfigModel.DeleteProfile(profiles,"poc:example.org:444");Check(!profiles.ContainsKey("server:poc:example.org:444:userPassword"),"profile deletion removes profile fields");
var before=profiles.ToJsonString();try { ConfigModel.SaveProfile(profiles,new("bad","example.org",0,"POC")); throw new Exception("invalid accepted"); } catch(InvalidOperationException) {} Check(before==profiles.ToJsonString(),"invalid profile atomicity");
Check(!AvatarPolicy.ValidJpeg([255,216,255,217]),"invalid avatar rejected");

var cache=Path.Combine(Path.GetTempPath(),"qra-config-dotnet-test-"+Guid.NewGuid().ToString("N")); try { var executable=AdbBundle.Extract(cache); Check(File.Exists(executable),"embedded official ADB extraction verifies hashes"); File.WriteAllText(executable,"tampered"); Check(new FileInfo(AdbBundle.Extract(cache)).Length>1000,"tampered cache repaired"); } finally { Directory.Delete(cache,true); }

var fallbackSettings=Configuration()["settings"]!.AsObject(); ConfigModel.SaveProfile(fallbackSettings,new("DMR","example.org",62030,"DMR")); fallbackSettings["selectedServer:DMR"]="dmr:example.org:62030"; ConfigModel.DeleteProfile(fallbackSettings,"dmr:example.org:62030"); Check(fallbackSettings["selectedServer:DMR"]!.GetValue<string>()=="dmr:qra-team.online:62030","deleted selected profile chooses builtin fallback");
var combined=Configuration(); var combinedSettings=combined["settings"]!.AsObject(); ConfigModel.SaveProfile(combinedSettings,new("FRN","frn.example.org",11024,"FRN")); ConfigModel.SaveProfile(combinedSettings,new("PoC","poc.example.org",443,"POC")); var combinedDraft=(JsonObject)combined.DeepClone(); ConfigModel.DeleteProfile(combinedDraft["settings"]!.AsObject(),"poc:poc.example.org:443"); ConfigModel.SaveProfile(combinedDraft["settings"]!.AsObject(),new("Changed FRN","frn.example.org",11024,"FRN"),"frn.example.org:11024"); var combinedMerged=ConfigModel.MergeScopes(combined,combinedDraft,["POC"])["settings"]!.AsObject(); Check(ConfigModel.ProfileRows(combinedMerged).Count==1 && ConfigModel.ProfileRows(combinedMerged)[0]!["name"]!.GetValue<string>()=="FRN","profile merge isolates selected protocol");
combinedSettings["selectedFrnServer"]="frn.example.org:11024"; var saved=combinedSettings.ToJsonString(); try { ConfigModel.DeleteProfile(combinedSettings,"frn.example.org:11024"); throw new Exception("active deletion accepted"); } catch(InvalidOperationException) {} Check(combinedSettings.ToJsonString()==saved,"active phone profile deletion blocked atomically");

await ApkInstallTests.Run();
if(args.Contains("--download")) {
    foreach(var terminal in new[]{false,true}) { var path=await new ApkInstallService().DownloadAsync(terminal,new Progress<string>(Console.WriteLine));try { Console.WriteLine("PASS actual public download/hash/package "+(terminal?"terminal_mode":"Global")+" "+await ApkInstallService.ValidateLocalAsync(path)); } finally { File.Delete(path); } }
} else if(args.Length==2 && args[0]=="--usb") await UsbSmokeTests.Run(args[1]);
else if(args.Length>0) Console.WriteLine("Actual APK package: "+await ApkInstallService.ValidateLocalAsync(args[0]));
