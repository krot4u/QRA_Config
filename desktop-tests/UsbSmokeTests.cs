using System.Text.Json.Nodes;
using QraConfig.Models;
using QraConfig.Services;

internal static class UsbSmokeTests
{
    public static async Task Run(string serial)
    {
        // Require an explicit physical USB target; BackendService independently verifies USB evidence.
        if(string.IsNullOrWhiteSpace(serial) || serial.Contains(':') || serial.StartsWith("emulator-",StringComparison.Ordinal)) throw new InvalidOperationException("Specify a physical USB device serial explicitly.");
        var backend = new BackendService(review:true); string token=""; ConfigSnapshot? original=null; bool writeAttempted=false, restored=false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(14));
        try {
            var session=await backend.BeginAsync(serial,timeout.Token);token=session.Token;
            Console.WriteLine("USB_CONSENT_PENDING: confirm the review-copy consent dialog on the selected device.");
            var deadline=DateTime.UtcNow.AddMinutes(10);
            while(true) {
                var status=await backend.StatusAsync(serial,token,timeout.Token);
                if(status.Status=="ready") break;
                if(status.Status=="closed") throw new InvalidOperationException("USB consent was declined or expired.");
                if(DateTime.UtcNow>=deadline) throw new InvalidOperationException("USB consent timeout.");
                await Task.Delay(1000,timeout.Token);
            }
            Console.WriteLine("PASS USB explicit consent ready");
            original=await backend.ReadAsync(serial,token,timeout.Token);
            if(!System.Text.RegularExpressions.Regex.IsMatch(original.Baseline,"^[0-9a-f]{64}$")) throw new InvalidOperationException("USB baseline is invalid.");
            Console.WriteLine("PASS USB read: baseline="+original.Baseline+" settings="+original.Settings.Count);
            var draft=original.DeepCopy();var originalTheme=original.Settings["appTheme"]?.GetValue<string>();
            draft.Settings["appTheme"]=originalTheme=="Dark"?"Light":"Dark";
            writeAttempted=true;await backend.ApplyAsync(serial,token,original,draft,["COMMON"],timeout.Token);
            await WaitApplied(backend,serial,token,timeout.Token);
            var applied=await backend.ReadAsync(serial,token,timeout.Token);
            if(!JsonNode.DeepEquals(applied.Settings["appTheme"],draft.Settings["appTheme"])) throw new InvalidOperationException("USB changed theme was not read back.");
            var before=original.DeepCopy();var after=applied.DeepCopy();before.Settings.Remove("appTheme");after.Settings.Remove("appTheme");
            if(!JsonNode.DeepEquals(before.Config,after.Config)) throw new InvalidOperationException("USB changed an unrelated or protected setting.");
            Console.WriteLine("PASS USB COMMON write/readback: all unrelated and protected settings preserved");
            await Restore(backend,serial,token,original,timeout.Token);restored=true;
            Console.WriteLine("PASS USB original full configuration restored and read back");
        }
        finally {
            try {
                if(writeAttempted && !restored && original is not null) {
                    using var recovery=new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    await Restore(backend,serial,token,original,recovery.Token);
                    Console.WriteLine("USB_RECOVERY_PASS: original full configuration restored");
                }
            }
            finally {
                if(token.Length>0) { using var end=new CancellationTokenSource(TimeSpan.FromSeconds(30));await backend.EndAsync(serial,token,end.Token);Console.WriteLine("PASS USB session ended"); }
            }
        }
    }
    private static async Task WaitApplied(BackendService backend,string serial,string token,CancellationToken ct)
    {
        for(var i=0;i<120;i++) {
            var status=await backend.StatusAsync(serial,token,ct);
            if(status.Status=="closed") throw new InvalidOperationException("USB session closed during write.");
            if(status.Operation=="error") throw new InvalidOperationException("USB phone rejected configuration write.");
            if(status.Operation=="success") return;
            await Task.Delay(1000,ct);
        }
        throw new InvalidOperationException("USB write did not finish within two minutes.");
    }
    private static async Task Restore(BackendService backend,string serial,string token,ConfigSnapshot original,CancellationToken ct)
    {
        // An interrupted write can still be running; never race its completion.
        for(var i=0;i<120;i++) { var status=await backend.StatusAsync(serial,token,ct);if(status.Operation!="applying")break;await Task.Delay(1000,ct); }
        var current=await backend.ReadAsync(serial,token,ct);
        if(!JsonNode.DeepEquals(current.Config,original.Config)) {
            var restore=current.DeepCopy();
            if(original.Settings.ContainsKey("appTheme")) restore.Settings["appTheme"]=original.Settings["appTheme"]?.DeepClone(); else restore.Settings.Remove("appTheme");
            await backend.ApplyAsync(serial,token,current,restore,["COMMON"],ct);await WaitApplied(backend,serial,token,ct);
        }
        var verified=await backend.ReadAsync(serial,token,ct);
        if(!JsonNode.DeepEquals(verified.Config,original.Config)) throw new InvalidOperationException("USB restoration incomplete: full configuration differs from the original.");
    }
}
