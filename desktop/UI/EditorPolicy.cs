using System.Text.Json.Nodes;
using QraConfig.Models;

namespace QraConfig.Desktop;
public static class EditorPolicy
{
    public static IReadOnlyList<ServerProfile> Profiles(JsonObject settings, string mode)
    {
        var rows = new List<ServerProfile>();
        if (mode == "FRN") rows.Add(new("QRA / FRN", "frn.qra-team.online", 11024, mode, true, Icon: "radio"));
        if (mode == "DMR") rows.Add(new("QRA-XLX / DMR", "qra-team.online", 62030, mode, true, Icon: "radio"));
        if (mode == "P25") rows.Add(new("QRA-P25", "p25.qra-team.online", 443, mode, true, Icon: "radio"));
        rows.AddRange(ConfigModel.ProfileRows(settings).Select(x => x!.AsObject()).Where(x => (x["protocol"]?.GetValue<string>() ?? "FRN") == mode)
            .Select(x => new ServerProfile(x["name"]!.GetValue<string>(), x["address"]!.GetValue<string>(), x["port"]!.GetValue<int>(), mode,
                Charset: x["charset"]?.GetValue<string>() ?? "cp1251", Icon: x["icon"]?.GetValue<string>() ?? "cloud")).OrderBy(x => x.Name, StringComparer.CurrentCulture));
        return rows;
    }
    public static bool Owns(string key, string profileId) =>
        profileId == "frn.qra-team.online:11024" && key.StartsWith("user", StringComparison.Ordinal) ||
        key.StartsWith("server:" + profileId + ":", StringComparison.Ordinal) ||
        new[] { "profileAudio:", "dmrOptions:", "serverIcon:", "pocAvatar:" }.Any(prefix => key == prefix + profileId);
    public static bool ProfileKey(string key) => key.StartsWith("user", StringComparison.Ordinal) ||
        new[] { "server:", "profileAudio:", "dmrOptions:", "serverIcon:", "pocAvatar:" }.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal));
    public static string RoomDefault(string mode) => mode switch { "DMR" => "9", "P25" => "496", "FRN" => "Test", _ => "" };
    public static string AudioDefault => """{"micGain":1.0,"bassDb":0.0,"trebleDb":0.0,"automaticMicLevel":true}""";
    public static string DmrDefault => """{"essid":0,"slot":2,"colorCode":1,"module":"A","privateCall":false,"rxAutomaticGain":true,"rxGainDb":0.0,"micSource":6,"qraMicFilter":false}""";
    public static void InitializeProfile(JsonObject settings, ServerProfile profile)
    {
        foreach (string field in new[] { "userEmail", "userPassword", "userNetwork" }.Concat(profile.Protocol == "DMR" ? new[] { "userCallsign" } : Array.Empty<string>()))
        {
            var key = $"server:{profile.Id}:{field}";
            if (!settings.ContainsKey(key)) settings[key] = field == "userNetwork" ? RoomDefault(profile.Protocol) : "";
        }
        if (!settings.ContainsKey("profileAudio:" + profile.Id)) settings["profileAudio:" + profile.Id] = AudioDefault;
        if (profile.Protocol == "DMR" && !settings.ContainsKey("dmrOptions:" + profile.Id)) settings["dmrOptions:" + profile.Id] = DmrDefault;
        if (!settings.ContainsKey("selectedServer:" + profile.Protocol)) settings["selectedServer:" + profile.Protocol] = profile.Id;
    }
}
