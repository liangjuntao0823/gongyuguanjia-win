using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 微信 H5 分享（v1.0：对齐 iOS v1.82 shareCard 语义）
// - 路径：页面/封面/首帧/二维码全走 users/<用户名>/shares/<房号>/，媒体走共享区 h/<房号>/
// - 视频统一转 mp4（ffmpeg），首帧图独立；媒体上传幂等（云端已有跳过）
// - 封面：首图压 400px JPEG；二维码：名片/链接二维码（用户私有 qrcode.jpg）
// - 页面文件名 = 房号_模式_时间戳.html，链接带 v 版本参数（微信重新抓卡片）
// - 水印 = HTML 覆盖层（文字/位置/透明度/字号，设置页配置）
// - 链接有效期：预签名 10 年（微信缓存），策略有效期记录在 ShareRecord（设置页 1/2/3/7/15/30 天）
// ============================================================

public enum ShareMode { Masked, Full }

public sealed class ShareMediaFile
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "image";   // image / video
    public string LocalPath { get; set; } = "";
}

public static class ShareSettings
{
    // 分享水印
    public const string KeyWatermark = "share.watermark";
    public static string WatermarkText
    {
        get => Settings.Get(KeyWatermark + ".text", "");
        set => Settings.Set(KeyWatermark + ".text", value);
    }
    public static string WatermarkPosition
    {
        get => Settings.Get(KeyWatermark + ".position", "bottom-right");
        set => Settings.Set(KeyWatermark + ".position", value);
    }
    public static double WatermarkOpacity
    {
        get => double.TryParse(Settings.Get(KeyWatermark + ".opacity", "0.35"), out var v) ? v : 0.35;
        set => Settings.Set(KeyWatermark + ".opacity", value.ToString("0.##"));
    }
    public static int WatermarkFontSize
    {
        get => int.TryParse(Settings.Get(KeyWatermark + ".fontSize", "14"), out var v) ? v : 14;
        set => Settings.Set(KeyWatermark + ".fontSize", value.ToString());
    }
    public static string WatermarkHash()
        => CosService.Sha256Hex($"{WatermarkText}|{WatermarkPosition}|{WatermarkOpacity}|{WatermarkFontSize}");

    // 选房链接有效期（天）：1/2/3/7/15/30
    public const string KeyLinkExpiry = "rooms.linkExpiry";
    public static int LinkExpiryDays
    {
        get => int.TryParse(Settings.Get(KeyLinkExpiry, "7"), out var v) ? v : 7;
        set => Settings.Set(KeyLinkExpiry, value.ToString());
    }

    // 名片信息
    public const string KeyCardName = "share.cardName";
    public const string KeyCardPhone = "share.cardPhone";
    public static string CardName
    {
        get => Settings.Get(KeyCardName, "梁涛");
        set => Settings.Set(KeyCardName, value);
    }
    public static string CardPhone
    {
        get => Settings.Get(KeyCardPhone, "13288251132");
        set => Settings.Set(KeyCardPhone, value);
    }

    // 压缩上传（房源+分享）
    public const string KeyCompress = "share.compressMedia";
    public static bool CompressMedia
    {
        get => Settings.Get(KeyCompress) == "1";
        set => Settings.Set(KeyCompress, value ? "1" : "0");
    }
}

/// 分享缓存（iOS ShareCacheStore 简化：记录上次分享的关键渲染参数与 URL）
public sealed class ShareCacheEntry
{
    public string RoomKey { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "";
    public string CustomDomain { get; set; } = "";
    public bool CompressOnShare { get; set; }
    public int TemplateVersion { get; set; }
    public bool QrcodeSet { get; set; }
    public string WatermarkHash { get; set; } = "";
    public string ShareUrl { get; set; } = "";
    public List<string> MediaFiles { get; set; } = new();
}

public static class ShareCacheStore
{
    private const string Prefix = "shareCache.";
    public static ShareCacheEntry? Load(string roomKey, string mode)
    {
        try
        {
            var json = Settings.Get(Prefix + roomKey + "." + mode);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<ShareCacheEntry>(json);
        }
        catch { return null; }
    }
    public static void Save(ShareCacheEntry e)
        => Settings.Set(Prefix + e.RoomKey + "." + e.Mode, JsonSerializer.Serialize(e));
}

public static class ShareService
{
    public const int TemplateVersion = 1;
    /// 预签名有效期 10 年（微信缓存卡片，iOS 3650*86400）
    private const int LinkExpires = 3650 * 86400;

    /// 生成分享链接（等价 iOS shareCard）。失败抛异常，调用方提示。
    public static async Task<string> GenerateShareAsync(House house, List<MediaItem> media, ShareMode mode)
    {
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) throw new InvalidOperationException("未配置云端");
        if (string.IsNullOrEmpty(cfg.CustomDomain))
            throw new InvalidOperationException("分享链接必须配置自定义域名（设置→云端同步→自定义域名）");
        if (media.Count == 0)
            throw new InvalidOperationException("这套房源没有媒体，无法生成分享页面");

        var modeLabel = mode == ShareMode.Masked ? "脱敏" : "完整";
        var compress = ShareSettings.CompressMedia;

        // 0. 缓存命中：关键参数未变 → 直接复用上次链接
        var hasQR = true; // Windows 端总是可生成二维码
        if (ShareCacheStore.Load(house.MediaRoomKey, modeLabel) is { } cached &&
            cached.Bucket == cfg.Bucket && cached.Region == cfg.Region &&
            cached.CustomDomain == cfg.CustomDomain && cached.CompressOnShare == compress &&
            cached.TemplateVersion == TemplateVersion && cached.QrcodeSet == hasQR &&
            cached.WatermarkHash == ShareSettings.WatermarkHash() &&
            !string.IsNullOrEmpty(cached.ShareUrl))
            return cached.ShareUrl;

        // 1. 需上传的媒体：媒体对象与信息无关，只上传新增
        var cachedPrev = ShareCacheStore.Load(house.MediaRoomKey, modeLabel);
        var cachedNames = cachedPrev?.MediaFiles?.ToHashSet() ?? new HashSet<string>();
        var toUpload = media.Where(m => !cachedNames.Contains(m.FileName)).ToList();

        // 2. 媒体排序：视频置顶
        var ordered = media.OrderBy(m => m.Kind == "video" ? 0 : 1).ToList();

        // 3. 上传媒体（幂等）+ 视频 mp4 + 首帧图
        var videoTags = new List<string>();
        var imageUrls = new List<string>();
        foreach (var item in ordered)
        {
            var localPath = Path.Combine(DataRoot.RoomMediaDirectory(house.MediaRoomKey), item.FileName);
            if (!File.Exists(localPath)) continue;
            var isVideo = item.Kind == "video";
            var remoteName = isVideo ? SyncService.CloudVideoName(item.FileName) : item.FileName;
            var remotePath = $"{CosService.CloudPrefix}{house.MediaRoomKey}/{remoteName}";

            if (toUpload.Any(m => m.FileName == item.FileName))
            {
                if (!await CosService.RemoteObjectExistsAsync(cfg, remotePath))
                    await UploadShareMediaAsync(cfg, house.MediaRoomKey, item.FileName, item.Kind, localPath, compress);
                // 视频首帧图：每个页面重新生成上传（iOS 一致）
                if (isVideo)
                {
                    var thumb = SyncService.MakeVideoThumb(localPath);
                    if (thumb != null)
                        await CosService.PutObjectAsync(cfg, SyncService.VthumbRemotePath(house.MediaRoomKey, item.FileName), thumb, "image/jpeg");
                }
            }
            var absUrl = CosService.PresignedUrl(cfg, remotePath, "GET", LinkExpires);
            if (isVideo)
            {
                var thumbPath = SyncService.VthumbRemotePath(house.MediaRoomKey, item.FileName);
                var thumbUrl = absUrl;
                try { thumbUrl = CosService.PresignedUrl(cfg, thumbPath, "GET", LinkExpires); } catch { }
                videoTags.Add($"<div class=\"vwrap\" onclick=\"playVideo(this,'{EscapeHtml(absUrl)}')\"><div class=\"vthumb\" style=\"background-image:url('{EscapeHtml(thumbUrl)}')\"></div><div class=\"vplay\">▶</div><video class=\"v\" controls controlsList=\"nodownload\" disablepictureinpicture playsinline webkit-playsinline preload=\"none\"></video></div>");
            }
            else
            {
                imageUrls.Add(absUrl);
            }
        }
        if (videoTags.Count == 0 && imageUrls.Count == 0)
            throw new InvalidOperationException("没有可用的媒体文件");

        // 4. 封面：第一张照片压 400px JPEG → users/<用户名>/shares/<房号>/cover
        var coverSrc = imageUrls.FirstOrDefault() ?? "";
        var firstImage = ordered.FirstOrDefault(m => m.Kind == "image");
        if (firstImage != null)
        {
            var srcPath = Path.Combine(DataRoot.RoomMediaDirectory(house.MediaRoomKey), firstImage.FileName);
            if (File.Exists(srcPath) && MakeCoverJpeg(srcPath) is { } coverData)
            {
                var coverPath = $"{SyncService.SafeUserName()}/shares/{house.MediaRoomKey}/cover";
                try
                {
                    await CosService.PutObjectAsync(cfg, coverPath, coverData, "image/jpeg");
                    coverSrc = CosService.PresignedUrl(cfg, coverPath, "GET", LinkExpires);
                }
                catch { /* 封面失败不阻塞：沿用第一张原图 */ }
            }
        }

        // 4.1 二维码（用户私有 qrcode.jpg）：内容 = 最终分享链接（先占位，上传页面后再补）
        var qrSrc = "";

        // 5. 生成并上传分享页
        var wm = WatermarkOverlayHtml(ShareSettings.WatermarkText, ShareSettings.WatermarkPosition,
            ShareSettings.WatermarkOpacity, ShareSettings.WatermarkFontSize);
        var pageName = $"{SafeFileName(house.DisplayRoomNo)}_{modeLabel}_{DateTime.Now:yyyyMMddHHmmss}.html";
        var pagePath = $"{SyncService.SafeUserName()}/shares/{house.MediaRoomKey}/{pageName}";
        var shareUrl = CosService.PresignedUrl(cfg, pagePath, "GET", LinkExpires);
        var versionedUrl = shareUrl + (shareUrl.Contains('?') ? "&" : "?") + "v=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var name = ShareSettings.CardName;
        var phone = ShareSettings.CardPhone;

        string html;
        if (mode == ShareMode.Masked)
        {
            var title = LayoutTitle(house.Layout, house.DisplayRoomNo);
            var desc = $"利保米兰公馆\n{name}-{phone}";
            html = BuildMaskedHtml(title, desc, name, phone, videoTags, imageUrls, coverSrc, qrSrc,
                versionedUrl, wm, TrackScript(house.DisplayRoomNo, modeLabel));
        }
        else
        {
            var title = $"{house.Layout}｜{house.DisplayRoomNo}";
            var desc = $"利保米兰公馆\n{name}-{phone}";
            html = BuildFullHtml(title, desc, house, name, phone, videoTags, imageUrls, coverSrc, qrSrc,
                versionedUrl, wm, TrackScript(house.DisplayRoomNo, modeLabel));
        }
        await CosService.PutObjectAsync(cfg, pagePath, Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8");

        // 6. 二维码重新生成（内容 = 最终 versionedUrl）并上传（此时再拼进页面一次会多一次上传；
        //    iOS 二维码是名片二维码与页面无关，Windows 端生成「名片二维码」等价）
        var qrBytes = MakeQrPng(versionedUrl);
        if (qrBytes != null)
        {
            try
            {
                await CosService.PutObjectAsync(cfg, $"{SyncService.SafeUserName()}/qrcode.jpg", qrBytes, "image/jpeg");
                qrSrc = CosService.PresignedUrl(cfg, $"{SyncService.SafeUserName()}/qrcode.jpg", "GET", LinkExpires);
            }
            catch { }
        }

        // 记录分享记录（本地 + 后续云函数上报）
        try
        {
            AppDb.AddShareRecord(house.DisplayRoomNo, modeLabel, versionedUrl);
        }
        catch { }

        // 缓存
        ShareCacheStore.Save(new ShareCacheEntry
        {
            RoomKey = house.MediaRoomKey,
            Mode = modeLabel,
            Bucket = cfg.Bucket,
            Region = cfg.Region,
            CustomDomain = cfg.CustomDomain,
            CompressOnShare = compress,
            TemplateVersion = TemplateVersion,
            QrcodeSet = hasQR,
            WatermarkHash = ShareSettings.WatermarkHash(),
            ShareUrl = versionedUrl,
            MediaFiles = media.Select(m => m.FileName).ToList(),
        });
        return versionedUrl;
    }

    // ---------- 媒体上传（幂等 + 视频 mp4 + 图片压缩） ----------

    private static async Task UploadShareMediaAsync(CosConfig cfg, string roomKey, string fileName, string kind, string localPath, bool compress)
    {
        var isVideo = kind == "video";
        var remoteName = isVideo ? SyncService.CloudVideoName(fileName) : fileName;
        var remotePath = $"{CosService.CloudPrefix}{roomKey}/{remoteName}";
        if (await CosService.RemoteObjectExistsAsync(cfg, remotePath)) return;

        byte[] data;
        string contentType;
        if (isVideo)
        {
            // 视频一律转 mp4；压缩开关开 → 720p
            var mp4 = compress ? TranscodeVideo720(localPath) : SyncService.TranscodeToMp4(localPath);
            data = mp4 ?? File.ReadAllBytes(localPath);
            contentType = "video/mp4";
        }
        else
        {
            if (compress && CompressImage(localPath, 1600) is { } img)
            {
                data = img; contentType = "image/jpeg";
            }
            else
            {
                data = File.ReadAllBytes(localPath);
                var lower = fileName.ToLowerInvariant();
                contentType = lower.EndsWith(".png") ? "image/png" : lower.EndsWith(".gif") ? "image/gif" : lower.EndsWith(".webp") ? "image/webp" : "image/jpeg";
            }
        }
        await CosService.PutObjectAsync(cfg, remotePath, data, contentType);
        if (isVideo)
        {
            var thumb = SyncService.MakeVideoThumb(localPath);
            if (thumb != null)
                await CosService.PutObjectAsync(cfg, SyncService.VthumbRemotePath(roomKey, fileName), thumb, "image/jpeg");
        }
    }

    // ---------- ffmpeg 图像/视频处理 ----------

    public static byte[]? TranscodeVideo720(string srcPath)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            RunFfmpeg($"-y -i \"{srcPath}\" -vf \"scale='min(1280,iw)':'min(720,ih)':force_original_aspect_ratio=decrease\" -c:v libx264 -preset veryfast -crf 26 -c:a aac -b:a 96k -movflags +faststart \"{tmp}\"");
            if (!File.Exists(tmp)) return null;
            var data = File.ReadAllBytes(tmp);
            try { File.Delete(tmp); } catch { }
            return data;
        }
        catch { return null; }
    }

    /// 图片压缩：长边 ≤ maxSide，JPEG q 合适质量
    public static byte[]? CompressImage(string srcPath, int maxSide)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            RunFfmpeg($"-y -i \"{srcPath}\" -vf \"scale='min({maxSide},iw)':'-2'\" -q:v 4 \"{tmp}\"");
            if (!File.Exists(tmp)) return null;
            var data = File.ReadAllBytes(tmp);
            try { File.Delete(tmp); } catch { }
            return data;
        }
        catch { return null; }
    }

    private static byte[]? MakeCoverJpeg(string srcPath)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            RunFfmpeg($"-y -i \"{srcPath}\" -vf \"scale=400:-1\" -q:v 4 \"{tmp}\"");
            if (!File.Exists(tmp)) return null;
            var data = File.ReadAllBytes(tmp);
            try { File.Delete(tmp); } catch { }
            return data;
        }
        catch { return null; }
    }

    private static void RunFfmpeg(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg");
        proc.WaitForExit(300000);
        if (!proc.HasExited) { try { proc.Kill(); } catch { } throw new InvalidOperationException("ffmpeg 超时"); }
        if (proc.ExitCode != 0) throw new InvalidOperationException($"ffmpeg 退出码 {proc.ExitCode}");
    }

    // ---------- 二维码（QRCoder） ----------

    public static byte[]? MakeQrPng(string content)
    {
        try
        {
            using var qr = new QRCoder.QRCodeGenerator();
            var data = qr.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var code = new QRCoder.PngByteQRCode(data);
            return code.GetGraphic(12);
        }
        catch { return null; }
    }

    // ---------- HTML 模板（iOS 同构） ----------

    public static string EscapeHtml(string s)
        => System.Net.WebUtility.HtmlEncode(s ?? "");

    public static string SafeFileName(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
            else if (ch is ' ' or '.' or '/' or ':' or ',' or '，') sb.Append('-');
        }
        return sb.Length == 0 ? "house" : sb.ToString();
    }

    public static string LayoutTitle(string layout, string roomNo)
    {
        var l = string.IsNullOrEmpty(layout) ? "房源" : layout;
        return $"{l}｜{roomNo}";
    }

    public static string WatermarkOverlayHtml(string text, string position, double opacity, int fontSize)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var posCss = position switch
        {
            "top-left" => "top:12px;left:12px;",
            "top-right" => "top:12px;right:12px;",
            "bottom-left" => "bottom:12px;left:12px;",
            "center" => "top:50%;left:50%;transform:translate(-50%,-50%);",
            _ => "bottom:12px;right:12px;",
        };
        return $"<div class=\"wm\" style=\"position:fixed;{posCss}color:rgba(255,255,255,{opacity.ToString("0.##")});font-size:{fontSize}px;font-weight:600;text-shadow:0 1px 3px rgba(0,0,0,.5);z-index:99;pointer-events:none;letter-spacing:1px;\">{EscapeHtml(text)}</div>";
    }

    public static string TrackScript(string room, string mode)
    {
        var url = CosConfig.Load().FunctionUrl ?? "";
        var uname = EscapeHtml(SyncService.SafeUserName());
        var roomEsc = EscapeHtml(room);
        var modeEsc = EscapeHtml(mode);
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
        return $"<script>(function(){{try{{var u='{url}';if(!u||u.indexOf('http')!==0)return;var k='gy_tk_'+encodeURIComponent('{roomEsc}')+'_'+encodeURIComponent('{modeEsc}');var now=Date.now(),last=0;try{{last=parseInt(localStorage.getItem(k)||'0',10)||0;}}catch(e){{}}if(now-last<30000)return;try{{localStorage.setItem(k,String(now));}}catch(e){{}}var r=(document.referrer||'').slice(0,120);var img=new Image();img.src=u+'?act=track&room='+encodeURIComponent('{roomEsc}')+'&mode='+encodeURIComponent('{modeEsc}')+'&ref='+encodeURIComponent(r)+'&t='+now+'&uname='+encodeURIComponent('{uname}');}}catch(e){{}})();</script>";
    }

    private static string MediaBlockHtml(List<string> videoTags, List<string> imageUrls, string wm)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"media\">");
        foreach (var v in videoTags) sb.Append(v);
        foreach (var img in imageUrls)
            sb.Append($"<div class=\"cellwrap\" onclick=\"showImg('{EscapeHtml(img)}')\"><img class=\"cell\" src=\"{EscapeHtml(img)}\" loading=\"lazy\" alt=\"\"></div>");
        sb.Append(wm);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static string QrColumnHtml(string qrSrc)
    {
        if (string.IsNullOrEmpty(qrSrc)) return "";
        return $"<div class=\"qr-col\"><img class=\"qr\" src=\"{EscapeHtml(qrSrc)}\" alt=\"\"><div class=\"qr-tip\">扫码联系我</div></div>";
    }

    private static string BuildMaskedHtml(string title, string desc, string name, string phone,
        List<string> videoTags, List<string> imageUrls, string coverSrc, string qrSrc, string shareLink,
        string wm, string trackScript)
    {
        var mediaBlock = MediaBlockHtml(videoTags, imageUrls, wm);
        var coverMeta = string.IsNullOrEmpty(coverSrc) ? "" : $"<meta property=\"og:image\" content=\"{EscapeHtml(coverSrc)}\">";
        var urlMeta = string.IsNullOrEmpty(shareLink) ? "" : $"<meta property=\"og:url\" content=\"{EscapeHtml(shareLink)}\">";
        var qrCol = QrColumnHtml(qrSrc);
        return $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta property="og:title" content="{{EscapeHtml(title)}}">
        <meta property="og:description" content="{{EscapeHtml(desc)}}">
        {{coverMeta}}
        {{urlMeta}}
        <title>{{EscapeHtml(title)}}</title>
        <style>
        body{font-family:-apple-system,BlinkMacSystemFont,"PingFang SC","Helvetica Neue",sans-serif;margin:0;background:#f4f5f7;color:#1c1c1e;-webkit-touch-callout:none;-webkit-user-select:none;user-select:none;-webkit-tap-highlight-color:transparent;}
        .wrap{max-width:640px;margin:0 auto;padding:0 16px 16px;}
        .hero{background:linear-gradient(135deg,#1f3a5f,#2d5f8a);color:#fff;border-radius:16px;padding:20px 20px 16px;}
        .type{font-size:30px;font-weight:700;margin:0 0 14px;}
        .loc{display:flex;align-items:center;gap:8px;color:rgba(255,255,255,.92);text-decoration:none;}
        .sec{font-size:15px;color:#3a3a3c;margin:18px 4px 10px;font-weight:600;}
        .media{background:#fff;border-radius:16px;padding:12px;}
        .vwrap{position:relative;width:100%;aspect-ratio:4/3;border-radius:10px;overflow:hidden;margin-bottom:10px;background:#000;cursor:pointer;}
        .vthumb{position:absolute;inset:0;background-size:cover;background-position:center;}
        .vplay{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:64px;height:64px;border-radius:50%;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;color:#fff;font-size:28px;border:2px solid rgba(255,255,255,.85);}
        .vwrap video{width:100%;height:100%;display:none;background:#000;}
        .vwrap.playing .vthumb,.vwrap.playing .vplay{display:none;}
        .vwrap.playing video{display:block;}
        .cellwrap{width:100%;aspect-ratio:4/3;border-radius:10px;overflow:hidden;background-color:#e8e8ec;margin-bottom:10px;cursor:pointer;position:relative;}
        .cell{width:100%;height:100%;object-fit:cover;display:block;pointer-events:none;-webkit-user-drag:none;-webkit-touch-callout:none;}
        .card{background:linear-gradient(180deg,#f7fafd,#e9f1f9);border:1px solid #dfe9f3;border-radius:16px;padding:14px;margin-top:12px;}
        .qr-wrap{display:flex;align-items:center;gap:14px;}
        .qr-wrap.center{justify-content:center;}
        .qr-col{display:flex;flex-direction:column;align-items:center;gap:3px;flex-shrink:0;}
        .qr-tip{font-size:10px;color:#8e8e93;text-align:center;max-width:108px;white-space:nowrap;line-height:1.3;}
        .qr{width:108px;height:108px;border-radius:10px;object-fit:cover;flex-shrink:0;background:#fff;border:1px solid #e3e3e8;-webkit-touch-callout:default;-webkit-user-select:text;user-select:text;-webkit-user-drag:auto;}
        .card-info{flex:1;min-width:0;text-align:center;}
        .tag{color:#8e8e93;font-size:12px;margin-bottom:10px;}
        .row{display:flex;align-items:center;justify-content:center;gap:8px;}
        .name{font-size:17px;font-weight:600;}
        .num{font-size:16px;font-weight:600;color:#b8860b;}
        .note{color:#8e8e93;font-size:12px;margin:10px 0 14px;}
        .btns{display:flex;gap:10px;justify-content:center;}
        .btn{flex:1;max-width:220px;padding:12px 0;border-radius:999px;font-size:15px;font-weight:600;text-align:center;text-decoration:none;}
        .btn.call{background:#1f3a5f;color:#fff;}
        .btn.copy{background:#f0f0f2;color:#1c1c1e;border:none;}
        .foot{text-align:center;color:#8e8e93;font-size:12px;padding:18px 0 26px;}
        .wm{position:fixed;color:rgba(255,255,255,.35);font-size:14px;font-weight:600;text-shadow:0 1px 3px rgba(0,0,0,.5);z-index:99;pointer-events:none;letter-spacing:1px;}
        </style>
        </head>
        <body>
        <div class="wrap">
          <div class="hero">
            <h1 class="type">{{EscapeHtml(title)}}</h1>
            <div class="loc"><span class="loc-name">利保米兰公馆</span></div>
          </div>
          <div class="sec">实拍图</div>
          {{mediaBlock}}
          <div class="card {{(qrCol.Length > 0 ? "qr-wrap" : "qr-wrap center")}}">
            {{qrCol}}
            <div class="card-info">
              <div class="tag">业主直租 · 无中介费</div>
              <div class="row"><span class="name">{{EscapeHtml(name)}}</span></div>
              <div class="row"><span class="num">{{EscapeHtml(phone)}}</span></div>
              <div class="note">看房请提前联系</div>
              <div class="btns">
                <a class="btn call" href="tel:{{EscapeHtml(phone)}}">电话联系</a>
                <a class="btn copy" href="weixin://dl/business/?t=">{{EscapeHtml(phone)}}</a>
              </div>
            </div>
          </div>
          <div class="foot">本页由公寓管家生成</div>
        </div>
        <script>
        function playVideo(wrap,src){wrap.classList.add('playing');var v=wrap.querySelector('video');v.src=src;v.play();}
        function showImg(src){var d=document.createElement('div');d.style.cssText='position:fixed;inset:0;background:rgba(0,0,0,.92);z-index:999;display:flex;align-items:center;justify-content:center;';var i=document.createElement('img');i.src=src;i.style.cssText='max-width:100%;max-height:100%;object-fit:contain;';d.appendChild(i);d.onclick=function(){d.remove();};document.body.appendChild(d);}
        </script>
        {{trackScript}}
        </body>
        </html>
        """;
    }

    private static string BuildFullHtml(string title, string desc, House house, string name, string phone,
        List<string> videoTags, List<string> imageUrls, string coverSrc, string qrSrc, string shareLink,
        string wm, string trackScript)
    {
        var mediaBlock = MediaBlockHtml(videoTags, imageUrls, wm);
        var coverMeta = string.IsNullOrEmpty(coverSrc) ? "" : $"<meta property=\"og:image\" content=\"{EscapeHtml(coverSrc)}\">";
        var urlMeta = string.IsNullOrEmpty(shareLink) ? "" : $"<meta property=\"og:url\" content=\"{EscapeHtml(shareLink)}\">";
        var qrCol = QrColumnHtml(qrSrc);
        var statusText = house.Status;
        var remarkHtml = string.IsNullOrEmpty(house.Remark)
            ? ""
            : $"<div class=\"info-item\" style=\"grid-column:1/-1;\"><div class=\"k\">备注</div><div class=\"v\">{EscapeHtml(house.Remark)}</div></div>";
        return $$"""
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta property="og:title" content="{{EscapeHtml(title)}}">
        <meta property="og:description" content="{{EscapeHtml(desc)}}">
        {{coverMeta}}
        {{urlMeta}}
        <title>{{EscapeHtml(title)}}</title>
        <style>
        body{font-family:-apple-system,BlinkMacSystemFont,"PingFang SC","Helvetica Neue",sans-serif;margin:0;background:#f4f5f7;color:#1c1c1e;-webkit-touch-callout:none;-webkit-user-select:none;user-select:none;-webkit-tap-highlight-color:transparent;}
        .wrap{max-width:640px;margin:0 auto;padding:0 16px 16px;}
        .hero{background:linear-gradient(135deg,#1f3a5f,#2d5f8a);color:#fff;border-radius:16px;padding:20px 20px 16px;}
        .type{font-size:28px;font-weight:700;margin:0 0 10px;}
        .loc{display:flex;align-items:center;gap:8px;color:rgba(255,255,255,.92);font-size:14px;}
        .info-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:14px;}
        .info-item{background:rgba(255,255,255,.14);border-radius:10px;padding:10px 12px;}
        .info-item .k{font-size:11px;color:rgba(255,255,255,.7);}
        .info-item .v{font-size:16px;font-weight:600;margin-top:2px;}
        .sec{font-size:15px;color:#3a3a3c;margin:18px 4px 10px;font-weight:600;}
        .media{background:#fff;border-radius:16px;padding:12px;}
        .vwrap{position:relative;width:100%;aspect-ratio:4/3;border-radius:10px;overflow:hidden;margin-bottom:10px;background:#000;cursor:pointer;}
        .vthumb{position:absolute;inset:0;background-size:cover;background-position:center;}
        .vplay{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:64px;height:64px;border-radius:50%;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;color:#fff;font-size:28px;border:2px solid rgba(255,255,255,.85);}
        .vwrap video{width:100%;height:100%;display:none;background:#000;}
        .vwrap.playing .vthumb,.vwrap.playing .vplay{display:none;}
        .vwrap.playing video{display:block;}
        .cellwrap{width:100%;aspect-ratio:4/3;border-radius:10px;overflow:hidden;background-color:#e8e8ec;margin-bottom:10px;cursor:pointer;position:relative;}
        .cell{width:100%;height:100%;object-fit:cover;display:block;pointer-events:none;-webkit-user-drag:none;-webkit-touch-callout:none;}
        .card{background:linear-gradient(180deg,#f7fafd,#e9f1f9);border:1px solid #dfe9f3;border-radius:16px;padding:14px;margin-top:12px;}
        .qr-wrap{display:flex;align-items:center;gap:14px;}
        .qr-wrap.center{justify-content:center;}
        .qr-col{display:flex;flex-direction:column;align-items:center;gap:3px;flex-shrink:0;}
        .qr-tip{font-size:10px;color:#8e8e93;text-align:center;max-width:108px;white-space:nowrap;line-height:1.3;}
        .qr{width:108px;height:108px;border-radius:10px;object-fit:cover;flex-shrink:0;background:#fff;border:1px solid #e3e3e8;}
        .card-info{flex:1;min-width:0;text-align:center;}
        .tag{color:#8e8e93;font-size:12px;margin-bottom:10px;}
        .row{display:flex;align-items:center;justify-content:center;gap:8px;}
        .name{font-size:17px;font-weight:600;}
        .num{font-size:16px;font-weight:600;color:#b8860b;}
        .note{color:#8e8e93;font-size:12px;margin:10px 0 14px;}
        .btns{display:flex;gap:10px;justify-content:center;}
        .btn{flex:1;max-width:220px;padding:12px 0;border-radius:999px;font-size:15px;font-weight:600;text-align:center;text-decoration:none;}
        .btn.call{background:#1f3a5f;color:#fff;}
        .btn.copy{background:#f0f0f2;color:#1c1c1e;border:none;}
        .foot{text-align:center;color:#8e8e93;font-size:12px;padding:18px 0 26px;}
        .wm{position:fixed;color:rgba(255,255,255,.35);font-size:14px;font-weight:600;text-shadow:0 1px 3px rgba(0,0,0,.5);z-index:99;pointer-events:none;letter-spacing:1px;}
        </style>
        </head>
        <body>
        <div class="wrap">
          <div class="hero">
            <h1 class="type">{{EscapeHtml(title)}}</h1>
            <div class="loc">利保米兰公馆 · {{EscapeHtml(statusText)}}</div>
            <div class="info-grid">
              <div class="info-item"><div class="k">月租金</div><div class="v">¥{{house.Price}}</div></div>
              <div class="info-item"><div class="k">房东</div><div class="v">{{EscapeHtml(house.Landlord)}}</div></div>
              <div class="info-item"><div class="k">进门密码</div><div class="v">{{EscapeHtml(house.DoorPassword)}}</div></div>
              <div class="info-item"><div class="k">户型</div><div class="v">{{EscapeHtml(house.Layout)}}</div></div>
            </div>
            {{remarkHtml}}
          </div>
          <div class="sec">实拍图</div>
          {{mediaBlock}}
          <div class="card {{(qrCol.Length > 0 ? "qr-wrap" : "qr-wrap center")}}">
            {{qrCol}}
            <div class="card-info">
              <div class="tag">业主直租 · 无中介费</div>
              <div class="row"><span class="name">{{EscapeHtml(name)}}</span></div>
              <div class="row"><span class="num">{{EscapeHtml(phone)}}</span></div>
              <div class="note">看房请提前联系</div>
              <div class="btns">
                <a class="btn call" href="tel:{{EscapeHtml(phone)}}">电话联系</a>
                <a class="btn copy" href="weixin://dl/business/?t=">微信联系</a>
              </div>
            </div>
          </div>
          <div class="foot">本页由公寓管家生成</div>
        </div>
        <script>
        function playVideo(wrap,src){wrap.classList.add('playing');var v=wrap.querySelector('video');v.src=src;v.play();}
        function showImg(src){var d=document.createElement('div');d.style.cssText='position:fixed;inset:0;background:rgba(0,0,0,.92);z-index:999;display:flex;align-items:center;justify-content:center;';var i=document.createElement('img');i.src=src;i.style.cssText='max-width:100%;max-height:100%;object-fit:contain;';d.appendChild(i);d.onclick=function(){d.remove();};document.body.appendChild(d);}
        </script>
        {{trackScript}}
        </body>
        </html>
        """;
    }
}
