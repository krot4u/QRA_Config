using System.Text.Json.Nodes;

namespace QraConfig.Models;

public static class ConfigModel
{
    public static readonly string[] Scopes = ["COMMON","FRN","DMR","P25","POC"];
    public static readonly HashSet<string> ProtectedKeys = new(StringComparer.Ordinal) { "pttKeyCode","pttScanCode","pttDeviceId","pttKeyLabel","scanKeyCode","scanScanCode","scanKeyLabel","modeKeyCode","modeScanCode","modeKeyLabel","selectedFrnServer" };
    public static string ScopeFor(string key) {
        if(key.StartsWith("selectedServer:")) return key[15..];
        if(key.StartsWith("dmrOptions:")) return "DMR";
        if(key.StartsWith("pocAvatar:")) return "POC";
        if(key.StartsWith("server:") || key.StartsWith("profileAudio:") || key.StartsWith("serverIcon:")) {
            var id=key[(key.IndexOf(':')+1)..]; return id.StartsWith("dmr:")?"DMR":id.StartsWith("p25:")?"P25":id.StartsWith("poc:")?"POC":"FRN";
        }
        return key.StartsWith("user",StringComparison.Ordinal)?"FRN":"COMMON";
    }
    public static JsonArray ProfileRows(JsonObject settings) {
        var text=settings["frnServers"]?.GetValue<string>(); if(string.IsNullOrEmpty(text)) return new();
        try { return JsonNode.Parse(text)?.AsArray() ?? new(); } catch { throw new InvalidOperationException("Неверный список серверов."); }
    }
    public static string ServerId(JsonObject profile) => new ServerProfile(profile["name"]?.GetValue<string>()??"",profile["address"]!.GetValue<string>(),profile["port"]!.GetValue<int>(),profile["protocol"]?.GetValue<string>()??"FRN").Id;
    public static JsonObject MergeScopes(JsonObject original,JsonObject draft,IEnumerable<string> scopes) {
        var selected=scopes.ToHashSet(StringComparer.Ordinal); if(selected.Count==0 || selected.Except(Scopes).Any()) throw new InvalidOperationException("Выберите разделы для применения.");
        var result=(JsonObject)original.DeepClone(); var before=original["settings"]!.AsObject(); var edited=draft["settings"]!.AsObject(); var settings=result["settings"]!.AsObject();
        foreach(var key in before.Select(x=>x.Key).Union(edited.Select(x=>x.Key)).ToArray()) {
            if(key=="frnServers" || ProtectedKeys.Contains(key) || !selected.Contains(ScopeFor(key))) continue;
            if(edited.TryGetPropertyValue(key,out var value)) settings[key]=value?.DeepClone(); else settings.Remove(key);
        }
        var rows=new JsonArray();
        foreach(var row in ProfileRows(before).Concat(ProfileRows(edited))) {
            if(row is not JsonObject p) throw new InvalidOperationException("Неверный профиль сервера.");
        }
        foreach(var row in ProfileRows(before)) if(!selected.Contains(row!["protocol"]?.GetValue<string>()??"FRN")) rows.Add(row.DeepClone());
        foreach(var row in ProfileRows(edited)) if(selected.Contains(row!["protocol"]?.GetValue<string>()??"FRN")) rows.Add(row.DeepClone());
        string Canonical(JsonArray a) => new JsonArray(a.OrderBy(x=>ServerId(x!.AsObject()),StringComparer.Ordinal).Select(x=>x!.DeepClone()).ToArray()).ToJsonString();
        if(Canonical(rows)!=Canonical(ProfileRows(before))) settings["frnServers"]=rows.ToJsonString();
        Validate(settings); return result;
    }
    public static void Validate(JsonObject settings) {
        if(settings["appTheme"] is JsonValue theme && !new[]{"Dark","Light","System","Monochrome"}.Contains(theme.GetValue<string>())) throw new InvalidOperationException("Неизвестная тема.");
        var ids=new HashSet<string>(StringComparer.Ordinal); var icons=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in ProfileRows(settings)) {
            var p=row!.AsObject(); var mode=p["protocol"]?.GetValue<string>()??"FRN"; var address=p["address"]!.GetValue<string>(); var port=p["port"]!.GetValue<int>();
            if(!Scopes.Contains(mode) || mode=="COMMON" || string.IsNullOrWhiteSpace(p["name"]?.GetValue<string>()) || string.IsNullOrWhiteSpace(address) || address.Any(char.IsWhiteSpace) || address.Contains('/') || port is <1 or >65535 || !ids.Add(ServerId(p)) || new[]{"frn.qra-team.online:11024","dmr:qra-team.online:62030","p25:p25.qra-team.online:443"}.Contains(ServerId(p)) || !new[]{"cp1251","cp1252"}.Contains(p["charset"]?.GetValue<string>()) || !new[]{"radio","cloud","globe","satellite","router","hub","headset","flag","wifi","dns","bolt","star"}.Contains(p["icon"]?.GetValue<string>()) || mode!="POC" && p["icon"]?.GetValue<string>()=="radio" || !icons.Add(mode+":"+p["icon"]?.GetValue<string>())) throw new InvalidOperationException("Проверьте имя, адрес, порт и уникальность серверов.");
        }
        foreach(var entry in settings.Where(x=>x.Key.StartsWith("pocAvatar:"))) { var text=entry.Value?.GetValue<string>()??""; if(text.Length>0 && (!entry.Key.StartsWith("pocAvatar:poc:") || text.Length>49152 || text.Length%2!=0 || text.Any(x=>!Uri.IsHexDigit(x)))) throw new InvalidOperationException("Неверное личное фото."); if(text.Length>0 && !AvatarPolicy.ValidJpeg(Convert.FromHexString(text))) throw new InvalidOperationException("Фото должно быть JPEG 96×96 без EXIF."); }
    }
    public static void SaveProfile(JsonObject settings,ServerProfile profile,string? previousId=null) {
        if(profile.BuiltIn) throw new InvalidOperationException("Встроенный сервер нельзя изменять.");
        if(previousId is not null && previousId!=profile.Id && settings["selectedFrnServer"]?.GetValue<string>()==previousId) throw new InvalidOperationException("Сначала выберите другой профиль на телефоне и прочитайте настройки заново.");
        var rows=ProfileRows(settings); if(rows.Any(x=>ServerId(x!.AsObject())==profile.Id && profile.Id!=previousId)) throw new InvalidOperationException("Сервер уже существует.");
        var previous=rows.FirstOrDefault(x=>ServerId(x!.AsObject())==previousId); if(previous is not null) rows.Remove(previous);
        rows.Add(new JsonObject { ["name"]=profile.Name.Trim(),["address"]=profile.Address.Trim().ToLowerInvariant(),["port"]=profile.Port,["protocol"]=profile.Protocol,["charset"]=profile.Charset,["icon"]=profile.Icon });
        var draft=(JsonObject)settings.DeepClone(); draft["frnServers"]=rows.ToJsonString();
        if(previousId is not null && previousId!=profile.Id) MoveProfileData(draft,previousId,profile.Id);
        Validate(draft); Copy(settings,draft);
    }
    public static void DeleteProfile(JsonObject settings,string id) {
        if(settings["selectedFrnServer"]?.GetValue<string>()==id) throw new InvalidOperationException("Сначала выберите другой профиль на телефоне и прочитайте настройки заново.");
        var rows=ProfileRows(settings); var row=rows.FirstOrDefault(x=>ServerId(x!.AsObject())==id); if(row is null) throw new InvalidOperationException("Можно удалить только собственный сервер.");
        rows.Remove(row); var draft=(JsonObject)settings.DeepClone(); draft["frnServers"]=rows.ToJsonString(); MoveProfileData(draft,id,null);
        var mode=row["protocol"]?.GetValue<string>()??"FRN"; var selection="selectedServer:"+mode;
        if(settings[selection]?.GetValue<string>()==id) { string? fallback=mode switch { "FRN"=>"frn.qra-team.online:11024", "DMR"=>"dmr:qra-team.online:62030", "P25"=>"p25:p25.qra-team.online:443", _=>rows.FirstOrDefault(x=>x!["protocol"]?.GetValue<string>()==mode) is JsonObject p ? ServerId(p):null }; if(fallback is not null) draft[selection]=fallback; }
        Copy(settings,draft);
    }
    private static void MoveProfileData(JsonObject settings,string oldId,string? newId) {
        foreach(var key in settings.Select(x=>x.Key).Where(k=>k.StartsWith("server:"+oldId+":") || k=="profileAudio:"+oldId || k=="dmrOptions:"+oldId || k=="serverIcon:"+oldId || k=="pocAvatar:"+oldId).ToArray()) { var value=settings[key]?.DeepClone(); settings.Remove(key); if(newId is not null) settings[key.Replace(oldId,newId,StringComparison.Ordinal)]=value; }
        foreach(var key in settings.Select(x=>x.Key).Where(k=>k.StartsWith("selectedServer:")).ToArray()) if(settings[key]?.GetValue<string>()==oldId) { if(newId is not null) settings[key]=newId; else settings.Remove(key); }
    }
    private static void Copy(JsonObject target,JsonObject source) { target.Clear(); foreach(var item in source) target[item.Key]=item.Value?.DeepClone(); }
}
