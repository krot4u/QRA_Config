using System.Text.Json.Nodes;

namespace QraConfig.Models;

public sealed record DeviceInfo(string Serial, string State, string Model)
{
    public string DisplayName => $"{Model.Replace('_', ' ')} · {Serial}";
    public bool IsReady => State == "device";
}
public sealed record SessionStatus(string Token, string Status, string Operation, string Message);
public sealed record ConfigSnapshot(string Baseline, JsonObject Config, JsonObject Metadata)
{
    public JsonObject Settings => (JsonObject)Config["settings"]!;
    public ConfigSnapshot DeepCopy() => new(Baseline, (JsonObject)Config.DeepClone(), (JsonObject)Metadata.DeepClone());
}
public sealed record ServerProfile(string Name, string Address, int Port, string Protocol, bool BuiltIn = false, string Charset = "cp1251", string Icon = "cloud", string ProfileId = "")
{
    public string Id => (Protocol == "FRN" ? "" : Protocol.ToLowerInvariant()+":") + Address.Trim().ToLowerInvariant()+(ProfileId.Length == 0 ? "" : "#"+ProfileId)+":"+Port;
}
