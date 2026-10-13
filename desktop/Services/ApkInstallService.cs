using System.IO;
using System.Net.Http;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QraConfig.Services;

public sealed record ApkRelease(Uri ApkUrl, long Size, string Sha256, int VersionCode);

public sealed class ApkInstallService
{
    public const string ProductionPackage = "com.example.qra_frn";
    public const string ReviewPackage = "com.example.qra_frn.review";
    public const long MaximumApkSize = 150 * 1024 * 1024;
    public const string ManifestUrl = "https://s3.twcstorage.ru/3fdfbe06-e875729c-b1d6-40b5-b774-c6a987279a1c/QRA_APP/qra-frn/android/latest.json";
    public static Uri GetManifestUri(bool terminal) => new(terminal ? ManifestUrl.Replace("/latest.json", "/pnc360/latest.json", StringComparison.Ordinal) : ManifestUrl);

    public static ApkRelease ParseManifest(string json, Uri manifestUri, bool terminal)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            var url = new Uri(root.GetProperty("apkUrl").GetString()!, UriKind.Absolute);
            var prefix = new Uri(manifestUri, ".").AbsolutePath;
            var size = root.GetProperty("size").GetInt64(); var hash = root.GetProperty("sha256").GetString()!;
            var version = root.GetProperty("versionCode").GetInt32();
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("packageName").GetString() != ProductionPackage || root.GetProperty("channel").GetString() != (terminal ? "terminal_mode" : "Global") || version <= 0 || size <= 0 || size > MaximumApkSize || hash.Length != 64 || !hash.All(Uri.IsHexDigit) || url.Scheme != "https" || url.Authority != manifestUri.Authority || url.UserInfo.Length != 0 || url.AbsolutePath.Contains('%') || url.Query.Length != 0 || url.Fragment.Length != 0 || !url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || !url.AbsolutePath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            return new(url, size, hash, version);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException or NullReferenceException)
        { throw new InvalidOperationException("Публичный манифест QRA-Talk некорректен. Установка отменена."); }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = async (context, ct) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, ct);
            foreach (var address in addresses) {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket:true); }
                catch { socket.Dispose(); if (ct.IsCancellationRequested) throw; }
            }
            throw new IOException("Не удалось подключиться к серверу QRA по IPv4.");
        }};
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<string> DownloadAsync(bool terminal, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try { return await DownloadCoreAsync(terminal, progress, ct); }
        catch (HttpRequestException) { throw new InvalidOperationException("Не удалось скачать QRA-Talk с сервера QRA. Проверьте подключение к Интернету или выберите локальный APK."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("Истекло время скачивания APK. Повторите загрузку или выберите локальный файл."); }
    }
    private async Task<string> DownloadCoreAsync(bool terminal, IProgress<string>? progress, CancellationToken ct)
    {
        using var client = CreateClient(); var manifestUri = GetManifestUri(terminal);
        progress?.Report("Проверка последнего публичного релиза QRA-Talk…");
        using var manifestResponse = await client.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, ct); manifestResponse.EnsureSuccessStatusCode();
        using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(ct);
        using var manifestBytes = new MemoryStream(); var buffer = new byte[65536]; int read;
        while ((read = await manifestStream.ReadAsync(buffer, ct)) > 0) { if (manifestBytes.Length + read > 65536) throw new InvalidOperationException("Манифест превышает допустимый размер."); manifestBytes.Write(buffer, 0, read); }
        var release = ParseManifest(Encoding.UTF8.GetString(manifestBytes.ToArray()), manifestUri, terminal);
        var directory = Path.Combine(Path.GetTempPath(), "QRA-Config-apk"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".apk");
        try {
            progress?.Report($"Скачивание QRA-Talk {release.VersionCode}…");
            using var response = await client.GetAsync(release.ApkUrl, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidOperationException("Размер APK не совпадает с манифестом.");
            using (var input = await response.Content.ReadAsStreamAsync(ct))
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync:true)) {
                long total = 0; while ((read = await input.ReadAsync(buffer, ct)) > 0) { total += read; if (total > release.Size) throw new InvalidOperationException("Размер APK не совпадает с манифестом."); await output.WriteAsync(buffer.AsMemory(0, read), ct); }
                if (total != release.Size) throw new InvalidOperationException("APK скачан не полностью.");
            }
            using (var file = File.OpenRead(path)) { var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)); if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Контрольная сумма APK не совпадает. Установка отменена."); }
            await ValidateLocalAsync(path, ct); return path;
        } catch { File.Delete(path); throw; }
    }

    public static async Task<string> ValidateLocalAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try {
            var info = new FileInfo(path); if (!info.Exists || info.Length <= 0 || info.Length > MaximumApkSize || !path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            using var zip = ZipFile.OpenRead(path); var manifests = zip.Entries.Where(x => x.FullName == "AndroidManifest.xml").ToArray();
            if (manifests.Length != 1 || manifests[0].Length > 1024 * 1024 || !zip.Entries.Any(x => x.FullName == "classes.dex")) throw new InvalidOperationException();
            using var stream = manifests[0].Open(); using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes, ct);
            var package = ReadPackage(bytes.ToArray());
            if (package != ProductionPackage && package != ReviewPackage) throw new InvalidOperationException();
            return package;
        } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException) { throw new InvalidOperationException("Выберите корректный APK QRA-Talk. Другие приложения устанавливать нельзя."); }
    }

    // Android binary XML: read the manifest's package attribute, rather than searching arbitrary ZIP bytes.
    internal static string ReadPackage(byte[] data)
    {
        ushort U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(o, 2));
        int U32(int o) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o, 4)));
        if (data.Length < 8 || U16(0) != 3 || U16(2) != 8 || U32(4) != data.Length) throw new InvalidDataException();
        string[] strings = [];
        for (var offset = (int)U16(2); offset < data.Length;) {
            var type = U16(offset); var header = U16(offset + 2); var size = U32(offset + 4);
            if (header < 8 || size < header || offset + size > data.Length) throw new InvalidDataException();
            if (type == 1) {
                var count = U32(offset + 8); if (count > 65536 || header < 28 || header + (long)count * 4 > size) throw new InvalidDataException();
                var utf8 = (U32(offset + 16) & 256) != 0; var start = U32(offset + 20); strings = new string[count];
                for (int i=0;i<count;i++) {
                    int p = offset + start + U32(offset + header + i * 4);
                    int Length8() { int n = data[p++]; return (n & 128) != 0 ? ((n & 127) << 8) | data[p++] : n; }
                    int Length16() { int n=U16(p); p+=2; if ((n & 32768) == 0) return n; int tail=U16(p); p+=2; return ((n & 32767) << 16) | tail; }
                    int length; if (utf8) { Length8(); length=Length8(); } else length=checked(Length16()*2);
                    if (p + (long)length >= offset + size) throw new InvalidDataException();
                    strings[i] = (utf8 ? Encoding.UTF8 : Encoding.Unicode).GetString(data,p,length);
                }
            }
            if (type == 0x102) {
                int extension = offset + header;
                if (strings[U32(extension + 4)] == "manifest") {
                    int attributeStart = U16(extension + 8), attributeSize = U16(extension + 10), count = U16(extension + 12);
                    if (attributeSize < 20 || extension + attributeStart + (long)count*attributeSize > offset+size) throw new InvalidDataException();
                    for (int i=0;i<count;i++) { int a=extension+attributeStart+i*attributeSize; if (strings[U32(a+4)] != "package") continue; uint raw=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(a+8,4)); if (raw != uint.MaxValue) return strings[checked((int)raw)]; if (data[a+15] == 3) return strings[U32(a+16)]; }
                }
            }
            offset += size;
        }
        throw new InvalidDataException();
    }
}
