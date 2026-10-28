using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 腾讯云 COS 请求层（v1.0：完全对齐 iOS v1.82 CosUploader 协议）
// - 预签名 URL（Authorization 参数走 URL，非 Header）
// - rawPath 未编码参与签名，请求 URL 用 urlPathAllowed 编码（斜杠保留）
// - headers（host + content-type…）与 query params 按字典序参与签名段
// - ListObjectsV2 支持 continuation-token 翻页 + encoding-type=url + CommonPrefixes
// - 404 与网络错误严格区分（CosObjectNotFoundException vs 异常）
// ============================================================

public sealed class CosObjectNotFoundException : Exception
{
    public string Path { get; }
    public CosObjectNotFoundException(string path) : base($"云端对象不存在: {path}") { Path = path; }
}

public static class CosService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    // ---------- Host 与编码（iOS v1.19/v1.20 语义） ----------

    /// 请求 Host：配置了自定义域名就用它（剥离 https:// http:// 前缀与尾部斜杠），否则 COS 默认域名。
    public static string Host(CosConfig c)
    {
        var domain = (c.CustomDomain ?? "").Trim();
        if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            domain = domain["https://".Length..];
        else if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            domain = domain["http://".Length..];
        domain = domain.TrimEnd('/');
        if (!string.IsNullOrEmpty(domain)) return domain;
        return $"{c.Bucket}.cos.{c.Region}.myqcloud.com";
    }

    /// RFC 3986 严格百分号编码（保留 A-Z a-z 0-9 - _ . ~）
    public static string UrlEncode(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            var ch = (char)b;
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.' || ch == '~')
                sb.Append(ch);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    /// 路径编码（保留 /，等价 iOS .urlPathAllowed）
    public static string UrlPathEncode(string rawPath)
        => UrlEncode(rawPath).Replace("%2F", "/");

    public static string PercentDecode(string s)
    {
        try { return Uri.UnescapeDataString(s); } catch { return s; }
    }

    // ---------- 预签名 URL（iOS presignedURL 全量复刻） ----------

    public static string PresignedUrl(CosConfig c, string path, string method, int expires = 600,
        (string, string)[]? headers = null, (string, string)[]? paramsList = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signTime = $"{now};{now + expires}";
        var keyTime = signTime;
        var hostValue = Host(c);
        var rawPath = "/" + path;
        var encodedPath = UrlPathEncode(rawPath);

        var pairs = (headers ?? Array.Empty<(string, string)>())
            .Select(h => (Key: h.Item1.ToLowerInvariant(), Value: UrlEncode(h.Item2))).ToList();
        pairs.Add(("host", hostValue.ToLowerInvariant()));
        pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var headerList = string.Join(";", pairs.Select(p => p.Key));
        var headerValue = string.Join("&", pairs.Select(p => $"{p.Key}={p.Value}"));

        var sortedParams = (paramsList ?? Array.Empty<(string, string)>())
            .Select(p => (Key: p.Item1.ToLowerInvariant(), Value: UrlEncode(p.Item2))).ToList();
        sortedParams.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        var paramList = string.Join(";", sortedParams.Select(p => p.Key));
        var paramValue = string.Join("&", sortedParams.Select(p => $"{p.Key}={p.Value}"));

        var signKey = HmacSha1Base64(c.SecretKey, keyTime);
        // 路径必须用未编码的 rawPath 参与签名（官方规范）
        var httpString = $"{method.ToLowerInvariant()}\n{rawPath}\n{paramValue}\n{headerValue}\n";
        var sha1Http = Sha1Hex(httpString);
        var stringToSign = $"sha1\n{signTime}\n{sha1Http}\n";
        var signature = HmacSha1Hex(signKey, stringToSign);

        var query = new List<string>();
        query.AddRange(sortedParams.Select(p => $"{p.Key}={p.Value}"));
        query.Add("q-sign-algorithm=sha1");
        query.Add($"q-ak={UrlEncode(c.SecretId)}");
        query.Add($"q-sign-time={signTime}");
        query.Add($"q-key-time={keyTime}");
        query.Add($"q-header-list={headerList}");
        query.Add($"q-url-param-list={paramList}");
        query.Add($"q-signature={signature}");
        return $"https://{hostValue}{encodedPath}?{string.Join("&", query)}";
    }

    private static string HmacSha1Base64(string key, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string HmacSha1Hex(string key, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    private static string Sha1Hex(string data)
    {
        using var sha = SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    public static string Sha256Hex(string data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }

    // ---------- 对象操作 ----------

    public static async Task<bool> PutObjectAsync(CosConfig c, string key, byte[] data, string contentType)
    {
        var url = PresignedUrl(c, key, "PUT", 600, new[] { ("content-type", contentType) });
        using var req = new HttpRequestMessage(HttpMethod.Put, url);
        req.Headers.TryAddWithoutValidation("Content-Type", contentType);
        req.Content = new ByteArrayContent(data);
        using var resp = await Http.SendAsync(req);
        if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 409) return true;
        var body = await resp.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"上传失败（HTTP {(int)resp.StatusCode}）{ExtractCosError(body)}");
    }

    /// 拉取对象。404 → CosObjectNotFoundException；网络/非404 → 抛异常（iOS v1.42 严格区分）
    public static async Task<byte[]> FetchObjectAsync(CosConfig c, string key)
    {
        var url = PresignedUrl(c, key, "GET");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await Http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
            return await resp.Content.ReadAsByteArrayAsync();
        if ((int)resp.StatusCode == 404)
            throw new CosObjectNotFoundException(key);
        var body = await resp.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"云端读取失败（HTTP {(int)resp.StatusCode}）{ExtractCosError(body)}");
    }

    public static async Task<bool> DeleteObjectAsync(CosConfig c, string key)
    {
        var url = PresignedUrl(c, key, "DELETE");
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        using var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode || (int)resp.StatusCode == 404;
    }

    /// 幂等检查：桶内已存在 → 跳过不重复覆盖（iOS uploadHouseMedia 语义）
    public static async Task<bool> RemoteObjectExistsAsync(CosConfig c, string key)
    {
        var url = PresignedUrl(c, key, "HEAD");
        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    // ---------- 列表（ListObjectsV2，翻页 + CommonPrefixes，iOS 全量复刻） ----------

    public static async Task<List<(string Name, long Size, DateTime Date)>> ListObjectsAsync(
        CosConfig c, string prefix, string? delimiter = null)
    {
        var result = new List<(string, long, DateTime)>();
        string? continuationToken = null;
        do
        {
            var ps = new List<(string, string)>
            {
                ("list-type", "2"), ("prefix", prefix), ("encoding-type", "url")
            };
            if (!string.IsNullOrEmpty(delimiter)) ps.Add(("delimiter", delimiter!));
            if (!string.IsNullOrEmpty(continuationToken)) ps.Add(("continuation-token", continuationToken!));
            var url = PresignedUrl(c, "", "GET", 300, paramsList: ps.ToArray());
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"云端列表获取失败（HTTP {(int)resp.StatusCode}）{ExtractCosError(body)}");
            }
            var xml = await resp.Content.ReadAsStringAsync();
            var blocks = ExtractContentsBlocks(xml);
            foreach (var b in blocks)
            {
                var key = TagValue("Key", b);
                if (string.IsNullOrEmpty(key)) continue;
                var size = long.TryParse(TagValue("Size", b), out var s) ? s : 0;
                var date = ParseCosDate(TagValue("LastModified", b));
                result.Add((PercentDecode(key), size, date));
            }
            // v1.71：delimiter="/" 时 COS 用 CommonPrefixes 返回「目录子项」
            var prefixBlocks = ExtractCommonPrefixes(xml);
            foreach (var p in prefixBlocks)
                result.Add((PercentDecode(p), 0, DateTime.MinValue));

            var truncated = TagValue("IsTruncated", xml);
            var next = TagValue("NextContinuationToken", xml);
            if (truncated == "true" && !string.IsNullOrEmpty(next))
                continuationToken = PercentDecode(next);
            else
                continuationToken = null;
        } while (continuationToken != null);
        return result;
    }

    private static List<string> ExtractContentsBlocks(string xml)
    {
        var blocks = new List<string>();
        var rest = xml;
        while (true)
        {
            var start = rest.IndexOf("<Contents>", StringComparison.Ordinal);
            if (start < 0) break;
            var end = rest.IndexOf("</Contents>", start, StringComparison.Ordinal);
            if (end < 0) break;
            blocks.Add(rest.Substring(start + "<Contents>".Length, end - start - "<Contents>".Length));
            rest = rest[(end + "</Contents>".Length)..];
        }
        return blocks;
    }

    private static List<string> ExtractCommonPrefixes(string xml)
    {
        var res = new List<string>();
        var rest = xml;
        while (true)
        {
            var start = rest.IndexOf("<CommonPrefixes>", StringComparison.Ordinal);
            if (start < 0) break;
            var end = rest.IndexOf("</CommonPrefixes>", start, StringComparison.Ordinal);
            if (end < 0) break;
            var block = rest.Substring(start, end - start);
            var p = TagValue("Prefix", block);
            if (!string.IsNullOrEmpty(p)) res.Add(p);
            rest = rest[(end + "</CommonPrefixes>".Length)..];
        }
        return res;
    }

    private static string? TagValue(string tag, string xml)
    {
        var startTag = "<" + tag + ">";
        var endTag = "</" + tag + ">";
        var s = xml.IndexOf(startTag, StringComparison.Ordinal);
        if (s < 0) return null;
        s += startTag.Length;
        var e = xml.IndexOf(endTag, s, StringComparison.Ordinal);
        if (e < 0) return null;
        return xml.Substring(s, e - s);
    }

    private static DateTime ParseCosDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
    }

    public static string ExtractCosError(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        var code = TagValue("Code", xml) ?? "";
        var message = TagValue("Message", xml) ?? "";
        if (string.IsNullOrEmpty(code)) return "";
        return string.IsNullOrEmpty(message) ? $"错误码 {code}" : $"错误码 {code}：{message}";
    }

    // ---------- 云函数 ----------

    /// 云函数调用（iOS 语义：act/k/u… 全部走 query；k = SHA256(后台口令) 鉴权）
    public static async Task<string?> CallFunctionAsync(CosConfig c, Dictionary<string, string> query)
    {
        if (string.IsNullOrEmpty(c.FunctionUrl)) return null;
        var sep = c.FunctionUrl.Contains('?') ? "&" : "?";
        var url = c.FunctionUrl + sep + string.Join("&",
            query.Select(kv => $"{UrlEncode(kv.Key)}={UrlEncode(kv.Value)}"));
        try
        {
            using var resp = await Http.GetAsync(url);
            if ((int)resp.StatusCode == 401)
                throw new InvalidOperationException("后台口令不匹配（请到 设置→云端配置→后台口令 修改后重试）");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch (InvalidOperationException) { throw; }
        catch { return null; }
    }

    // ---------- 云端账本（对齐 iOS v1.72：一房一文件 h/<roomKey>/info.json） ----------

    public const string CloudPrefix = "h/";

    public static string RecordPath(string roomKey) => $"h/{roomKey}/info.json";

    /// iOS HouseStatus rawValue：available / rented
    public static string StatusToCloud(string status) => status switch
    {
        "已租" or "rented" => "rented",
        _ => "available",
    };

    public static string StatusFromCloud(string? raw)
        => string.IsNullOrEmpty(raw) || raw == "available" ? "待租" : "已租";

    // ---------- 本地账本镜像（iOS 同构 schema：英文 status、Double 时间戳、mediaNames） ----------

    public static void WriteLocalLedger(House h, List<MediaItem> media)
    {
        try
        {
            var payload = new
            {
                roomKey = h.MediaRoomKey,
                roomNo = h.RoomNo,
                tower = h.Tower,
                landlord = h.Landlord,
                layout = h.Layout,
                price = h.Price,
                password = h.DoorPassword,
                remark = h.Remark,
                status = StatusToCloud(h.Status),
                updatedAt = ((DateTimeOffset)h.LastUpdatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
                updatedBy = CosConfig.Load().Username,
                mediaNames = media.Select(m => m.FileName).ToList(),
            };
            File.WriteAllText(DataRoot.LedgerPath(h.MediaRoomKey),
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void DeleteLocalLedger(string roomKey)
    {
        try
        {
            var p = DataRoot.LedgerPath(roomKey);
            if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }

    /// 读取本地账本镜像（离线重建用；兼容旧中文 schema 与 iOS 英文 schema）
    public static House? ReadLocalLedger(string roomKey)
    {
        try
        {
            var p = DataRoot.LedgerPath(roomKey);
            if (!File.Exists(p)) return null;
            var json = File.ReadAllText(p);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var h = new House
            {
                MediaRoomKey = roomKey,
                RoomNo = GetStr(r, "roomNo"),
                Tower = GetInt(r, "tower", 1),
                Landlord = GetStr(r, "landlord"),
                Layout = GetStr(r, "layout"),
                Price = GetInt(r, "price", 0),
                DoorPassword = GetStr(r, "password"),
                Remark = GetStr(r, "remark"),
            };
            // status：英文 rawValue 或中文展示态都兼容
            var statusRaw = GetStr(r, "status");
            h.Status = StatusFromCloud(statusRaw);
            if (statusRaw is "待租" or "已上架" or "未上架" or "已租") h.Status = statusRaw;
            h.IsRented = h.Status == "已租";
            // 时间戳：Double（iOS）或 ISO8601（旧版）
            h.UpdatedAt = ReadTime(r, "updatedAt", DateTime.Now);
            h.LastUpdatedAt = ReadTime(r, "lastUpdatedAt", h.UpdatedAt);
            return h;
        }
        catch { return null; }
    }

    private static string GetStr(JsonElement r, string key)
        => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement r, string key, int def)
    {
        if (!r.TryGetProperty(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return def;
    }

    private static DateTime ReadTime(JsonElement r, string key, DateTime def)
    {
        if (!r.TryGetProperty(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
            return DateTimeOffset.FromUnixTimeSeconds((long)d).LocalDateTime;
        if (v.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return def;
    }

    /// 媒体文件命名（对齐两端：roomKey-TAG-uid-N.ext）
    public static string MakeMediaFileName(string roomKey, string sourceName, string kind, HashSet<string> used)
    {
        var ext = Path.GetExtension(sourceName).TrimStart('.').ToLowerInvariant();
        var tag = ext switch
        {
            "png" => "PNG",
            "heic" => "HEIC",
            "gif" => "GIF",
            "mp4" or "mov" or "m4v" => "MP4",
            _ => "JPG",
        };
        var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        int n = 1;
        var name = $"{roomKey}-{tag}-{uid}-{n}.{ext}";
        while (used.Contains(name)) { n++; name = $"{roomKey}-{tag}-{uid}-{n}.{ext}"; }
        return name;
    }
}
