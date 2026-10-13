using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace QraConfig.Services;

internal static class AdbBundle
{
    public static string Extract(string? cacheDirectory = null)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var archive = assembly.GetManifestResourceStream("QraConfig.Adb.zip") ?? throw new IOException("Нет встроенного ADB.");
        using var buffer = new MemoryStream(); archive.CopyTo(buffer); var bytes = buffer.ToArray();
        using var metadata = assembly.GetManifestResourceStream("QraConfig.Adb.manifest.json")!;
        var manifest = JsonNode.Parse(metadata)!.AsObject();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        // Hash of the embedded five-file subset; manifest.sha256 describes the original Google archive.
        if (hash != "71e57a6cf12addf67e0966bc0c13a6edb141e358a39a487cc11039109752beea") throw new IOException("Неверная контрольная сумма ADB.");
        var files = manifest["files"]!.AsObject();
        var dir = Path.Combine(cacheDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QRA-Config", "adb", hash);
        Directory.CreateDirectory(dir);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (!files.ContainsKey(entry.FullName) || Path.GetFileName(entry.FullName) != entry.FullName || entry.Length > 32*1024*1024)
                throw new IOException("Неверное содержимое ADB.");
            using var stream = entry.Open(); using var data = new MemoryStream(); stream.CopyTo(data);
            var content = data.ToArray(); var expected = files[entry.FullName]!.GetValue<string>();
            if (Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant() != expected) throw new IOException("Нарушена целостность ADB.");
            var target = Path.Combine(dir, entry.FullName);
            if (!File.Exists(target) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant() != expected)
            {
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temporary, content); File.Move(temporary, target, true);
            }
            found.Add(entry.FullName);
        }
        if (found.Count != files.Count) throw new IOException("Неполный комплект ADB.");
        return Path.Combine(dir, "adb.exe");
    }
}
