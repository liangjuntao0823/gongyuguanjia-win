using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 腾讯云 COS 签名与请求（对齐 Mac CosUploader 协议）
// 签名 V5：q-sign-algorithm=sha1&q-ak=..&q-sign-time=..&q-key-time=..&q-header-list=..&q-url-param-list=..&q-signature=..
// ============================================================

public static class CosService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string ObjectUrl(CosConfig c, string key)
        => $"https://{c.Bucket}.cos.{c.Region}.myqcloud.com/{Uri.EscapeDataString(key)}";

    public static string ObjectUrlWithDomain(CosConfig c, string key)
    {
        if (!string.IsNullOrEmpty(c.CustomDomain))
            return $"{c.CustomDomain.TrimEnd('/')}/{Uri.EscapeDataString(key)}";
        return ObjectUrl(c, key);
    }

    private static string Sign(CosConfig c, string method, string key, DateTime now, long expireSeconds = 600)
    {
        string keyName = "/" + key;
        long signTimeStart = ((DateTimeOffset)now).ToUnixTimeSeconds();
        long signTimeEnd = signTimeStart + expireSeconds;
        string signTime = $"{signTimeStart};{signTimeEnd}";
        string keyTime = signTime;
        string signKey = HmacSha1(c.SecretKey, keyTime);
        string formatKey = Uri.EscapeDataString(keyName);
        string httpString = $"{method.ToLowerInvariant()}\n{formatKey}\n\nhost={c.Bucket}.cos.{c.Region}.myqcloud.com\n";
        string sha1HttpString = Sha1Hex(httpString);
        string stringToSign = $"sha1\n{signTime}\n{sha1HttpString}\n";
        string signature = HmacSha1Hex(signKey, stringToSign);
        return $"q-sign-algorithm=sha1&q-ak={c.SecretId}&q-sign-time={signTime}&q-key-time={keyTime}&q-header-list=host&q-url-param-list=&q-signature={signature}";
    }

    private static string HmacSha1(string key, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string HmacSha1Hex(string key, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha1Hex(string data)
    {
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ---------- 对象操作 ----------

    public static async Task<bool> PutObjectAsync(CosConfig c, string key, byte[] data, string contentType)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, ObjectUrl(c, key));
        req.Headers.TryAddWithoutValidation("Authorization", Sign(c, "put", key, DateTime.Now));
        req.Headers.TryAddWithoutValidation("Content-Type", contentType);
        req.Content = new ByteArrayContent(data);
        var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode || (int)resp.StatusCode == 409;
    }

    public static async Task<byte[]?> GetObjectAsync(CosConfig c, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, ObjectUrl(c, key));
        req.Headers.TryAddWithoutValidation("Authorization", Sign(c, "get", key, DateTime.Now));
        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync();
    }

    public static async Task<bool> DeleteObjectAsync(CosConfig c, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, ObjectUrl(c, key));
        req.Headers.TryAddWithoutValidation("Authorization", Sign(c, "delete", key, DateTime.Now));
        var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    public static async Task<bool> HeadObjectAsync(CosConfig c, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Head, ObjectUrl(c, key));
        req.Headers.TryAddWithoutValidation("Authorization", Sign(c, "head", key, DateTime.Now));
        var resp = await Http.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }

    /// 列出对象。delimiter="/" 时只列目录前缀（对齐 iOS fetchAll 目录式列出，避免媒体对象全量拉取）
    public static async Task<List<string>> ListObjectsAsync(CosConfig c, string prefix, string delimiter = "")
    {
        var keys = new List<string>();
        var url = $"{ObjectUrl(c, "")}?list-type=2&prefix={Uri.EscapeDataString(prefix)}{(string.IsNullOrEmpty(delimiter) ? "" : "&delimiter=" + Uri.EscapeDataString(delimiter))}&max-keys=1000";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", Sign(c, "get", "", DateTime.Now));
        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return keys;
        var xml = await resp.Content.ReadAsStringAsync();
        // 解析 Key（delimiter 模式返回 CommonPrefixes/prefix）
        var keyMatches = System.Text.RegularExpressions.Regex.Matches(xml, "<Key>([^<]+)</Key>");
        foreach (System.Text.RegularExpressions.Match m in keyMatches)
            keys.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));
        if (!string.IsNullOrEmpty(delimiter))
        {
            var prefixMatches = System.Text.RegularExpressions.Regex.Matches(xml, "<Prefix>([^<]+)</Prefix>");
            foreach (System.Text.RegularExpressions.Match m in prefixMatches)
            {
                var p = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                if (!keys.Contains(p)) keys.Add(p);
            }
        }
        return keys;
    }

    // ---------- 云函数 ----------

    public static async Task<string?> CallFunctionAsync(CosConfig c, Dictionary<string, string> query)
    {
        if (string.IsNullOrEmpty(c.FunctionUrl)) return null;
        var url = c.FunctionUrl + (c.FunctionUrl.Contains('?') ? "&" : "?") + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        try
        {
            var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    // ---------- 云端账本（对齐 iOS v1.72：media/<房号>/info.json + 云端 h/<roomKey>/info.json） ----------

    public const string CloudPrefix = "h/";

    public static string RecordPath(string roomKey) => $"h/{roomKey}/info.json";

    /// 本地账本镜像（media/<roomKey>/info.json）
    public static void WriteLocalLedger(House h, List<MediaItem> media)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["roomNo"] = h.RoomNo,
                ["tower"] = h.Tower,
                ["landlord"] = h.Landlord,
                ["layout"] = h.Layout,
                ["price"] = h.Price,
                ["status"] = h.Status,
                ["isRented"] = h.IsRented,
                ["isListed"] = h.IsListed,
                ["remark"] = h.Remark,
                ["doorPassword"] = h.DoorPassword,
                ["leaseStart"] = h.LeaseStart,
                ["leaseEnd"] = h.LeaseEnd,
                ["isDeleted"] = h.IsDeleted,
                ["updatedAt"] = h.UpdatedAt.ToString("o"),
                ["lastUpdatedAt"] = h.LastUpdatedAt.ToString("o"),
                ["media"] = media.Select(m => new { name = m.FileName, kind = m.Kind }).ToList(),
            };
            File.WriteAllText(DataRoot.LedgerPath(h.MediaRoomKey),
                System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// 本地账本镜像删除（随软删清理）
    public static void DeleteLocalLedger(string roomKey)
    {
        try
        {
            var p = DataRoot.LedgerPath(roomKey);
            if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }

    /// 读取本地账本镜像（用于离线重建）
    public static House? ReadLocalLedger(string roomKey)
    {
        try
        {
            var p = DataRoot.LedgerPath(roomKey);
            if (!File.Exists(p)) return null;
            var json = File.ReadAllText(p);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new House
            {
                RoomNo = root.TryGetProperty("roomNo", out var rn) ? rn.GetString() ?? "" : "",
                Tower = root.TryGetProperty("tower", out var tw) ? tw.GetInt32() : 1,
                Landlord = root.TryGetProperty("landlord", out var ll) ? ll.GetString() ?? "" : "",
                Layout = root.TryGetProperty("layout", out var ly) ? ly.GetString() ?? "" : "",
                Price = root.TryGetProperty("price", out var pr) ? pr.GetInt32() : 0,
                Status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "待租" : "待租",
                IsRented = root.TryGetProperty("isRented", out var ir) && ir.GetBoolean(),
                IsListed = root.TryGetProperty("isListed", out var il) && il.GetBoolean(),
                Remark = root.TryGetProperty("remark", out var rm) ? rm.GetString() ?? "" : "",
                DoorPassword = root.TryGetProperty("doorPassword", out var dp) ? dp.GetString() ?? "" : "",
                LeaseStart = root.TryGetProperty("leaseStart", out var ls) ? ls.GetString() ?? "" : "",
                LeaseEnd = root.TryGetProperty("leaseEnd", out var le) ? le.GetString() ?? "" : "",
                IsDeleted = root.TryGetProperty("isDeleted", out var id) && id.GetBoolean(),
                UpdatedAt = root.TryGetProperty("updatedAt", out var ua) && DateTime.TryParse(ua.GetString(), out var d1) ? d1 : DateTime.Now,
                LastUpdatedAt = root.TryGetProperty("lastUpdatedAt", out var lu) && DateTime.TryParse(lu.GetString(), out var d2) ? d2 : DateTime.Now,
                MediaRoomKey = roomKey,
            };
        }
        catch { return null; }
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
