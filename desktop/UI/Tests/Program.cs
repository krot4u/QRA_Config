using System.Text.Json.Nodes;
using QraConfig.Desktop;
using QraConfig.Models;

int checks=0;
void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);checks++;}
var settings=JsonNode.Parse("""{"frnServers":"[]","selectedFrnServer":"frn.qra-team.online:11024","selectedServer:DMR":"dmr:qra-team.online:62030"}""")!.AsObject();
var profiles=new[]{
    new ServerProfile("FRN extra","frn.test",10024,"FRN"),
    new ServerProfile("DMR extra","dmr.test",62031,"DMR"),
    new ServerProfile("P25 extra","p25.test",41000,"P25"),
    new ServerProfile("PoC extra","poc.test",443,"POC")
};
foreach(var profile in profiles)ConfigModel.SaveProfile(settings,profile);
string before=settings.ToJsonString();
foreach(var mode in new[]{"FRN","DMR","P25","POC"}){
    var rows=EditorPolicy.Profiles(settings,mode);
    Check(rows.Count==(mode=="POC"?1:2),"Built-in/custom profile menu count "+mode);
    Check(rows.All(x=>x.Protocol==mode),"Profile menu must not leak another mode "+mode);
    Check(rows.Any(x=>x.Id==profiles.Single(x=>x.Protocol==mode).Id),"Custom profile is discoverable "+mode);
}
Check(EditorPolicy.Profiles(settings,"FRN")[0].Id=="frn.qra-team.online:11024","FRN built-in is available without custom row");
var ids=profiles.Select(x=>x.Id).Concat(new[]{"frn.qra-team.online:11024","dmr:qra-team.online:62030","p25:p25.qra-team.online:443"}).ToArray();
foreach(var id in ids){
    Check(EditorPolicy.Owns("profileAudio:"+id,id),"Own audio editable");
    Check(EditorPolicy.Owns("server:"+id+":userPassword",id),"Own masked credentials editable");
    foreach(var other in ids.Where(x=>x!=id))Check(!EditorPolicy.Owns("profileAudio:"+other,id),"Other profile audio is isolated");
    Check(!EditorPolicy.Owns("selectedServer:DMR",id),"UI profile choice is not phone default setting");
    Check(!EditorPolicy.Owns("selectedFrnServer",id),"UI profile choice is not active phone setting");
}
Check(EditorPolicy.Owns("userCallsign","frn.qra-team.online:11024"),"Top-level FRN identity belongs to QRA");
Check(!EditorPolicy.Owns("userCallsign",profiles[0].Id),"Top-level FRN identity does not belong to other FRN server");
Check(EditorPolicy.Owns("pocAvatar:poc:poc.test:443","poc:poc.test:443"),"Own PoC photo visible");
Check(!EditorPolicy.Owns("pocAvatar:poc:poc.test:4433","poc:poc.test:443"),"Partial IDs do not match");
Check(EditorPolicy.ProfileKey("dmrOptions:dmr:dmr.test:62031")&&!EditorPolicy.ProfileKey("selectedServer:DMR"),"Mode overview hides details and retains default selection");
Check(settings.ToJsonString()==before,"Browsing profiles does not mutate phone selection or settings");
foreach(var profile in profiles){
    EditorPolicy.InitializeProfile(settings,profile);
    foreach(var field in new[]{"userEmail","userPassword","userNetwork"})Check(settings.ContainsKey("server:"+profile.Id+":"+field),"New profile field editable: "+field);
    Check(settings["server:"+profile.Id+":userNetwork"]!.GetValue<string>()==EditorPolicy.RoomDefault(profile.Protocol),"Native mode room default");
    Check(JsonNode.Parse(settings["profileAudio:"+profile.Id]!.GetValue<string>())!["micGain"]!.GetValue<double>()==1,"New profile audio defaults");
    if(profile.Protocol=="DMR")Check(settings.ContainsKey("server:"+profile.Id+":userCallsign")&&settings.ContainsKey("dmrOptions:"+profile.Id),"DMR callsign and options available");
}
Check(settings["selectedServer:DMR"]!.GetValue<string>()=="dmr:qra-team.online:62030","Initializing an extra profile preserves existing phone default");

// New Android profiles may share an endpoint and an icon; preserve their independent namespaces.
foreach(var mode in new[]{"FRN","DMR","P25","POC"}) {
    var shared=new JsonObject { ["frnServers"]="[]" };
    var a=new ServerProfile("Shared A","shared.invalid",443,mode,ProfileId:"11111111-1111-4111-8111-111111111111");
    var b=a with { Name="Shared B",ProfileId="22222222-2222-4222-8222-222222222222" };
    ConfigModel.SaveProfile(shared,a);ConfigModel.SaveProfile(shared,b);
    shared["server:"+a.Id+":userEmail"]="first";shared["server:"+b.Id+":userEmail"]="second";
    var read=EditorPolicy.Profiles(shared,mode).Where(x=>!x.BuiltIn).ToArray();
    Check(read.Length==2 && read[0].Id==a.Id && read[1].Id==b.Id,"shared endpoint profile identifiers: "+mode);
    ConfigModel.SaveProfile(shared,b with { Name="Renamed" },b.Id);
    ConfigModel.DeleteProfile(shared,b.Id);
    Check(shared["server:"+a.Id+":userEmail"]?.GetValue<string>()=="first" && !shared.ContainsKey("server:"+b.Id+":userEmail"),"shared endpoint independent removal: "+mode);
}

Console.WriteLine($"PASS: {checks} editor policy checks, four-mode menus, exact profile isolation, no browse side effects, new profile fields/defaults.");
