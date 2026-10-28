using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 房源同步（v1.0：完全对齐 iOS v1.82 云端协议与语义）
// - 账本 schema：一房一文件 h/<roomKey>/info.json（iOS CloudHouseRecord 同构）
// - fetchAll：列 h/ 目录 → 并发 GET → 404 跳过 / 解析失败 throw（绝不误删本地）
// - 合并：最后写入优先；云端有本地无 → 新建（cloudStatus=cloud）+ 本地账本镜像
// - 删除联动：本地有云端无 → both/cloud 本地跟随删；local 聚合补传写云端
// - 媒体三名单对账（账本 / 本地记录 / 磁盘）：账本为准，下载失败绝不删云端
// - 视频统一转 mp4 + 补用户区首帧图（ffmpeg）；上传幂等（云端已有跳过）
// ============================================================

public sealed class CloudHouseRecord
{
    public string roomKey { get; set; } = "";
    public string roomNo { get; set; } = "";
    public int tower { get; set; }
    public string landlord { get; set; } = "";
    public string layout { get; set; } = "";
    public int price { get; set; }
    public string password { get; set; } = "";
    public string remark { get; set; } = "";
    public string status { get; set; } = "available";
    public double updatedAt { get; set; }
    public string updatedBy { get; set; } = "";
    public List<string> mediaNames { get; set; } = new();
}

public class SyncService
{
    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;   // 0-100
    public event Action? Completed;
    public event Action<string>? SyncLog;        // 逐条结果（差异预览 / 错误明细）

    private bool _running;
    public bool IsRunning => _running;
    public string StatusText { get; private set; } = "待同步";
    public List<string> DiffPreview { get; } = new();   // 同步差异摘要（预览用）

    public async Task<bool> SyncAsync(bool confirmOnly = false)
    {
        if (_running) return false;
        _running = true;
        DiffPreview.Clear();
        try
        {
            var cfg = CosConfig.Load();
            if (!cfg.IsValid)
            {
                SetStatus("未配置云端，跳过同步");
                return false;
            }
            SetStatus("正在同步…");
            ProgressChanged?.Invoke(5);

            // 1. 拉取云端房号目录（delimiter="/" 只列目录子项，不列媒体）
            var cloudRooms = await CosService.ListObjectsAsync(cfg, CosService.CloudPrefix, "/");
            var cloudRoomKeys = cloudRooms
                .Select(o => o.Name)
                .Where(n => n.StartsWith(CosService.CloudPrefix, StringComparison.Ordinal))
                .Select(n => n[CosService.CloudPrefix.Length..])
                .Where(n => n.EndsWith("/", StringComparison.Ordinal))
                .Select(n => n.TrimEnd('/'))
                .Where(n => n.Length > 0 && !n.StartsWith("_", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            // 2. 并发拉取云端账本（404 跳过；解析失败 throw）
            SetStatus("拉取云端账本…");
            var cloudLedgers = new ConcurrentDictionary<string, CloudHouseRecord>();
            var fetchTasks = cloudRoomKeys.Select(async key =>
            {
                try
                {
                    var data = await CosService.FetchObjectAsync(cfg, CosService.RecordPath(key));
                    var rec = JsonSerializer.Deserialize<CloudHouseRecord>(data)
                        ?? throw new InvalidOperationException("云端房源账本解析失败：info.json 格式异常");
                    if (string.IsNullOrEmpty(rec.roomKey)) rec.roomKey = key;
                    cloudLedgers[key] = rec;
                }
                catch (CosObjectNotFoundException) { /* 该套无账本 → 跳过 */ }
            }).ToArray();
            await Task.WhenAll(fetchTasks);
            ProgressChanged?.Invoke(30);

            // 3. 合并（iOS 语义：最后写入优先；本地补传聚合写）
            SetStatus("合并字段…");
            var localHouses = AppDb.GetHouses(true).Where(h => !string.IsNullOrEmpty(h.MediaRoomKey)).ToList();
            var localByRoom = localHouses.GroupBy(h => h.MediaRoomKey).ToDictionary(g => g.Key, g => g.First());
            var cloudKeys = cloudLedgers.Keys.ToHashSet();
            var toPush = new List<House>();

            foreach (var kv in cloudLedgers)
            {
                var rec = kv.Value;
                var key = kv.Key;
                if (localByRoom.TryGetValue(key, out var local))
                {
                    if (ApplyRecordToLocal(rec, local, out var changed))
                    {
                        DiffPreview.Add($"更新 {Display(local)}：{changed}");
                        AppDb.SaveHouse(local);
                        CosService.WriteLocalLedger(local, AppDb.GetMedia(key));
                    }
                }
                else
                {
                    var h = HouseFromRecord(rec);
                    h.CloudStatus = "cloud";
                    AppDb.SaveHouse(h);
                    CosService.WriteLocalLedger(h, new List<MediaItem>());
                    localByRoom[key] = h;
                    DiffPreview.Add($"新增 {Display(h)}（云端同步）");
                    if (rec.mediaNames.Count > 0) DiffPreview.Add($"媒体 {Display(h)}：待下载 {rec.mediaNames.Count} 项");
                }
            }

            // 4. 删除联动：本地有、云端无
            foreach (var h in localHouses.Where(h => !cloudKeys.Contains(h.MediaRoomKey)))
            {
                if (h.CloudStatus == "both" || h.CloudStatus == "cloud")
                {
                    // iOS：云端已删 → 本地跟随删除（删媒体目录 + 库记录 + 账本镜像）
                    MediaStoreDelete(h.MediaRoomKey);
                    AppDb.DeleteMedia(h.MediaRoomKey, null);
                    CosService.DeleteLocalLedger(h.MediaRoomKey);
                    AppDb.PermanentDeleteHouse(h.Id);
                    DiffPreview.Add($"删除 {Display(h)}（云端已删，本地跟随）");
                }
                else
                {
                    // v1.64：本地补传聚合为一次账本写入（并发写同一文件会互相覆盖丢房源）
                    toPush.Add(h);
                    DiffPreview.Add($"补传 {Display(h)}（仅本地→云端）");
                }
            }
            try { AppDb.NotifyHousesChanged(); } catch { }
            ProgressChanged?.Invoke(55);

            // 5. 本地补传聚合写账本（串行，成功后标 both）
            SetStatus("补传本地房源…");
            foreach (var h in toPush)
            {
                await PushHouseRecordAsync(cfg, h);
                h.CloudStatus = "both";
                AppDb.SaveHouse(h);
            }

            // 6. 媒体三名单对账
            SetStatus("对账媒体…");
            await ReconcileMediaAsync(cfg, cloudLedgers);
            ProgressChanged?.Invoke(85);

            // 7. 经营数据 + 远程配置（云函数，带口令鉴权）
            SetStatus("同步经营数据…");
            await SyncBusinessDataAsync(cfg);

            SetStatus("同步完成");
            ProgressChanged?.Invoke(100);
            Completed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message);
            return false;
        }
        finally
        {
            _running = false;
        }
    }

    // v1.0：同步预览（对齐 iOS applySyncPreview）——只读对比，不写库/不上传/不删除
    public async Task<List<string>> BuildPreviewAsync()
    {
        if (_running) return new List<string>();
        _running = true;
        DiffPreview.Clear();
        try
        {
            var cfg = CosConfig.Load();
            if (!cfg.IsValid) { DiffPreview.Add("未配置云端，跳过同步"); return DiffPreview; }

            var cloudRooms = await CosService.ListObjectsAsync(cfg, CosService.CloudPrefix, "/");
            var cloudRoomKeys = cloudRooms
                .Select(o => o.Name)
                .Where(n => n.StartsWith(CosService.CloudPrefix, StringComparison.Ordinal))
                .Select(n => n[CosService.CloudPrefix.Length..])
                .Where(n => n.EndsWith("/", StringComparison.Ordinal))
                .Select(n => n.TrimEnd('/'))
                .Where(n => n.Length > 0 && !n.StartsWith("_", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            var cloudLedgers = new ConcurrentDictionary<string, CloudHouseRecord>();
            var fetchTasks = cloudRoomKeys.Select(async key =>
            {
                try
                {
                    var data = await CosService.FetchObjectAsync(cfg, CosService.RecordPath(key));
                    var rec = JsonSerializer.Deserialize<CloudHouseRecord>(data)
                        ?? throw new InvalidOperationException("云端房源账本解析失败");
                    if (string.IsNullOrEmpty(rec.roomKey)) rec.roomKey = key;
                    cloudLedgers[key] = rec;
                }
                catch (CosObjectNotFoundException) { }
            }).ToArray();
            await Task.WhenAll(fetchTasks);

            var localHouses = AppDb.GetHouses(true).Where(h => !string.IsNullOrEmpty(h.MediaRoomKey)).ToList();
            var localByRoom = localHouses.GroupBy(h => h.MediaRoomKey).ToDictionary(g => g.Key, g => g.First());
            var cloudKeys = cloudLedgers.Keys.ToHashSet();

            foreach (var kv in cloudLedgers)
            {
                var rec = kv.Value;
                var key = kv.Key;
                if (localByRoom.TryGetValue(key, out var local))
                {
                    if (ApplyRecordToLocal(rec, local, out var changed))
                        DiffPreview.Add($"更新 {Display(local)}：{changed}");
                }
                else
                {
                    DiffPreview.Add($"新增 {Display(HouseFromRecord(rec))}（云端同步）");
                    if (rec.mediaNames.Count > 0) DiffPreview.Add($"媒体 {Display(HouseFromRecord(rec))}：待下载 {rec.mediaNames.Count} 项");
                }
            }
            foreach (var h in localHouses.Where(h => !cloudKeys.Contains(h.MediaRoomKey)))
            {
                if (h.CloudStatus == "both" || h.CloudStatus == "cloud")
                    DiffPreview.Add($"删除 {Display(h)}（云端已删，本地跟随删除）");
                else
                    DiffPreview.Add($"补传 {Display(h)}（仅本地→云端）");
            }
            return DiffPreview;
        }
        catch (Exception ex)
        {
            DiffPreview.Add("预览失败：" + ex.Message);
            return DiffPreview;
        }
        finally { _running = false; }
    }

    // ---------- 账本映射 ----------

    public static string Display(House h)
        => h.DisplayRoomNo + (h.Tower == 2 ? "（2座）" : "（1座）");

    private static House HouseFromRecord(CloudHouseRecord rec) => new()
    {
        RoomNo = rec.roomNo,
        Tower = rec.tower,
        Landlord = rec.landlord,
        Layout = rec.layout,
        Price = rec.price,
        DoorPassword = rec.password,
        Remark = rec.remark,
        Status = CosService.StatusFromCloud(rec.status),
        IsRented = rec.status == "rented",
        MediaRoomKey = rec.roomKey,
        LastUpdatedAt = FromCloudTime(rec.updatedAt),
        UpdatedAt = FromCloudTime(rec.updatedAt),
    };

    private static DateTime FromCloudTime(double ts)
        => ts > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)ts).LocalDateTime : DateTime.Now;

    private static bool ApplyRecordToLocal(CloudHouseRecord rec, House local, out string changed)
    {
        var diffs = new List<string>();
        if (local.RoomNo != rec.roomNo) { local.RoomNo = rec.roomNo; diffs.Add("房号"); }
        if (local.Tower != rec.tower) { local.Tower = rec.tower; diffs.Add("座"); }
        if (local.Landlord != rec.landlord) { local.Landlord = rec.landlord; diffs.Add("房东"); }
        if (local.Layout != rec.layout) { local.Layout = rec.layout; diffs.Add("户型"); }
        if (local.Price != rec.price) { local.Price = rec.price; diffs.Add("租金"); }
        if (local.DoorPassword != rec.password) { local.DoorPassword = rec.password; diffs.Add("密码"); }
        if (local.Remark != rec.remark) { local.Remark = rec.remark; diffs.Add("备注"); }
        var newStatus = CosService.StatusFromCloud(rec.status);
        if (local.Status != newStatus) { local.Status = newStatus; local.IsRented = newStatus == "已租"; diffs.Add("状态"); }
        local.LastUpdatedAt = FromCloudTime(rec.updatedAt);
        local.UpdatedAt = local.LastUpdatedAt;
        changed = string.Join("、", diffs);
        return diffs.Count > 0;
    }

    public static CloudHouseRecord RecordFromHouse(House h)
    {
        var cfg = CosConfig.Load();
        return new CloudHouseRecord
        {
            roomKey = h.MediaRoomKey,
            roomNo = h.RoomNo,
            tower = h.Tower,
            landlord = h.Landlord,
            layout = h.Layout,
            price = h.Price,
            password = h.DoorPassword,
            remark = h.Remark,
            status = CosService.StatusToCloud(h.Status),
            updatedAt = ((DateTimeOffset)h.LastUpdatedAt.ToUniversalTime()).ToUnixTimeSeconds(),
            updatedBy = string.IsNullOrWhiteSpace(cfg.Username) ? "未设置" : cfg.Username.Trim(),
            mediaNames = AppDb.GetMedia(h.MediaRoomKey).Select(m => m.FileName).ToList(),
        };
    }

    private async Task PushHouseRecordAsync(CosConfig cfg, House h)
    {
        var data = JsonSerializer.Serialize(RecordFromHouse(h));
        await CosService.PutObjectAsync(cfg, CosService.RecordPath(h.MediaRoomKey),
            System.Text.Encoding.UTF8.GetBytes(data), "application/json");
        CosService.WriteLocalLedger(h, AppDb.GetMedia(h.MediaRoomKey));
    }

    /// 软删联动：删除云端账本（媒体保留）——iOS 语义
    public static async Task PushHouseDeleteAsync(CosConfig cfg, string roomKey)
    {
        try { await CosService.DeleteObjectAsync(cfg, CosService.RecordPath(roomKey)); } catch { }
    }

    /// 彻底删除联动：删云端整套 h/<roomKey>/ 目录——iOS 清回收站语义
    public static async Task DeleteRemoteHouseDirectoryAsync(CosConfig cfg, string roomKey)
    {
        try
        {
            var objects = await CosService.ListObjectsAsync(cfg, $"{CosService.CloudPrefix}{roomKey}/");
            foreach (var o in objects)
                await CosService.DeleteObjectAsync(cfg, o.Name);
        }
        catch { }
    }

    // ---------- 媒体三名单对账（账本 / 本地记录 / 磁盘） ----------

    private async Task ReconcileMediaAsync(CosConfig cfg, ConcurrentDictionary<string, CloudHouseRecord> cloudLedgers)
    {
        var rooms = AppDb.GetHouses(true).Where(h => !string.IsNullOrEmpty(h.MediaRoomKey)).ToList();
        foreach (var h in rooms)
        {
            var key = h.MediaRoomKey;
            var roomDir = DataRoot.RoomMediaDirectory(key);
            Directory.CreateDirectory(roomDir);
            // 账本媒体清单（云端）优先；无账本的套（仅本地）以本地记录为准
            List<string> cloudNames;
            if (cloudLedgers.TryGetValue(key, out var rec))
                cloudNames = rec.mediaNames;
            else
                cloudNames = AppDb.GetMedia(key).Select(m => m.FileName).ToList();

            var localMedia = AppDb.GetMedia(key);
            var localNames = localMedia.Select(m => m.FileName).ToHashSet();
            var diskNames = Directory.Exists(roomDir)
                ? Directory.EnumerateFiles(roomDir).Select(Path.GetFileName).ToHashSet()
                : new HashSet<string>();

            // 账本有、本地记录缺 → 下载（失败绝不当作缺失删云端）
            foreach (var f in cloudNames)
            {
                if (localNames.Contains(f)) continue;
                var objKey = $"{CosService.CloudPrefix}{key}/{f}";
                byte[] data;
                try
                {
                    data = await CosService.FetchObjectAsync(cfg, objKey);
                }
                catch
                {
                    SyncLog?.Invoke($"下载失败（保留本地与云端）：{Display(h)}/{f}");
                    continue;
                }
                try { File.WriteAllBytes(Path.Combine(roomDir, f), data); } catch { }
                AppDb.AddMedia(new MediaItem
                {
                    RoomKey = key,
                    FileName = f,
                    Kind = f.ToLowerInvariant().EndsWith(".mp4") || f.ToLowerInvariant().EndsWith(".mov") ? "video" : "image",
                    Size = data.Length,
                });
                SyncLog?.Invoke($"下载媒体：{Display(h)}/{f}");
            }

            // 本地记录有、账本没有 → 移除本地记录（磁盘文件保留；iOS 收敛语义）
            foreach (var m in localMedia.Where(m => !cloudNames.Contains(m.FileName)).ToList())
                AppDb.DeleteMedia(key, m.FileName);

            // 本地记录有、磁盘缺 → 重新下载刷回（用户手删文件后刷新恢复）
            foreach (var f in localNames.Where(f => !diskNames.Contains(f) && cloudNames.Contains(f)))
            {
                try
                {
                    var data = await CosService.FetchObjectAsync(cfg, $"{CosService.CloudPrefix}{key}/{f}");
                    try { File.WriteAllBytes(Path.Combine(roomDir, f), data); } catch { }
                }
                catch { /* 网络失败保留记录，下次再刷 */ }
            }

            // 磁盘孤儿（磁盘有、记录无、账本无）→ 保留文件不入库（不污染云端；用户可手动清理）
            AppDb.UpdateHouseHasMedia(key);
        }
    }

    private static void MediaStoreDelete(string roomKey)
    {
        try
        {
            var dir = DataRoot.RoomMediaDirectory(roomKey);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
                // 目录没了再建回空目录（避免父级媒体目录结构被破坏）
                Directory.CreateDirectory(DataRoot.MediaDirectory);
            }
        }
        catch { }
    }

    // ---------- 视频转码（ffmpeg：统一 mp4 + 首帧图，iOS 对齐） ----------

    public static string CloudVideoName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return fileName[..^ext.Length] + ".mp4";
    }

    public static string VthumbRemotePath(string roomKey, string fileName)
        => $"users/{SafeUserName()}/shares/{roomKey}/{fileName}.vthumb.jpg";

    public static string SafeUserName()
    {
        var u = CosConfig.Load().Username?.Trim() ?? "";
        return string.IsNullOrEmpty(u) ? "未设置" : u;
    }

    /// 转 mp4；失败返回 null（调用方回退原样上传）
    public static byte[]? TranscodeToMp4(string srcPath)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            RunFfmpeg($"-y -i \"{srcPath}\" -c:v libx264 -preset veryfast -crf 26 -c:a aac -b:a 96k -movflags +faststart \"{tmp}\"");
            if (!File.Exists(tmp)) return null;
            var data = File.ReadAllBytes(tmp);
            try { File.Delete(tmp); } catch { }
            return data;
        }
        catch { return null; }
    }

    /// 视频首帧 JPEG（用户区 shares/<roomKey>/<name>.vthumb.jpg）
    public static byte[]? MakeVideoThumb(string srcPath)
    {
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            RunFfmpeg($"-y -i \"{srcPath}\" -vf \"thumbnail,scale=480:-1\" -frames:v 1 -q:v 4 \"{tmp}\"");
            if (!File.Exists(tmp)) return null;
            var data = File.ReadAllBytes(tmp);
            try { File.Delete(tmp); } catch { }
            return data;
        }
        catch { return null; }
    }

    private static void RunFfmpeg(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffmpeg");
        proc.WaitForExit(300000);
        if (!proc.HasExited) { try { proc.Kill(); } catch { } throw new InvalidOperationException("ffmpeg 超时"); }
        if (proc.ExitCode != 0) throw new InvalidOperationException($"ffmpeg 退出码 {proc.ExitCode}");
    }

    /// 媒体上传（幂等 + 视频转码 + 首帧图；iOS uploadHouseMedia 语义）
    public static async Task UploadMediaAsync(CosConfig cfg, string roomKey, string fileName, string kind)
    {
        var localPath = Path.Combine(DataRoot.RoomMediaDirectory(roomKey), fileName);
        if (!File.Exists(localPath)) return;
        var isVideo = kind == "video";
        var remoteName = isVideo ? CloudVideoName(fileName) : fileName;
        var remotePath = $"{CosService.CloudPrefix}{roomKey}/{remoteName}";
        if (await CosService.RemoteObjectExistsAsync(cfg, remotePath)) return; // 幂等跳过

        var contentType = "image/jpeg";
        byte[] data;
        if (isVideo)
        {
            var mp4 = TranscodeToMp4(localPath);
            if (mp4 != null) { data = mp4; contentType = "video/mp4"; }
            else { data = File.ReadAllBytes(localPath); contentType = "video/mp4"; }
        }
        else
        {
            data = File.ReadAllBytes(localPath);
            var lower = fileName.ToLowerInvariant();
            if (lower.EndsWith(".png")) contentType = "image/png";
            else if (lower.EndsWith(".gif")) contentType = "image/gif";
            else if (lower.EndsWith(".heic")) contentType = "image/heic";
            else if (lower.EndsWith(".webp")) contentType = "image/webp";
        }
        await CosService.PutObjectAsync(cfg, remotePath, data, contentType);
        // 视频同步补用户区首帧图（iOS 一致）
        if (isVideo)
        {
            var thumb = MakeVideoThumb(localPath);
            if (thumb != null)
                await CosService.PutObjectAsync(cfg, VthumbRemotePath(roomKey, fileName), thumb, "image/jpeg");
        }
    }

    // ---------- 经营数据 / 远程配置（云函数，k=SHA256(口令) 鉴权） ----------

    private async Task SyncBusinessDataAsync(CosConfig cfg)
    {
        try
        {
            if (string.IsNullOrEmpty(cfg.FunctionUrl)) return;
            var k = RemoteConfigService.PasswordHash();
            var u = CosConfig.Load().Username?.Trim() ?? "";
            var resp = await CosService.CallFunctionAsync(cfg, new Dictionary<string, string>
            {
                ["act"] = "configGet", ["u"] = u, ["k"] = k
            });
            if (!string.IsNullOrEmpty(resp))
            {
                RemoteConfigService.Apply(resp);
                SyncLog?.Invoke("远程配置已更新（公告/更新日志/最低版本）");
            }
        }
        catch (Exception ex)
        {
            SyncLog?.Invoke("经营数据同步：云函数未连通（" + ex.Message + "）");
        }
    }

    private void SetStatus(string s)
    {
        StatusText = s;
        StatusChanged?.Invoke(s);
    }
}

// ============================================================
// 远程配置（iOS RemoteConfig 对齐）+ 云端操作日志（CloudOps）
// ============================================================
public class RemoteConfigCache
{
    public string Notice { get; set; } = "";
    public string UpdateLog { get; set; } = "";
    public string MinimumVersion { get; set; } = "";
    public Dictionary<string, string> Help { get; set; } = new();
}

public static class RemoteConfigService
{
    private const string CacheKey = "remoteConfigCache";

    /// 云函数鉴权：k = SHA256(管理后台口令)（iOS CloudConfig.passwordHash）
    public static string PasswordHash()
        => CosService.Sha256Hex(CosConfig.Load().AdminPassword ?? "");

    /// act=configGet 响应（iOS RemoteConfig 同构）
    public static void Apply(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var ok = r.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
            if (!ok) return;
            var cache = new RemoteConfigCache
            {
                Notice = GetStr(r, "notice"),
                UpdateLog = GetStr(r, "updateLog"),
                MinimumVersion = GetStr(r, "minimumVersion"),
                Help = new Dictionary<string, string>(),
            };
            if (r.TryGetProperty("help", out var h) && h.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in h.EnumerateObject())
                    cache.Help[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : "";
            }
            Settings.Save(CacheKey, cache);
        }
        catch { }
    }

    public static string Help(string key, string fallback)
    {
        try
        {
            var cache = Settings.Load<RemoteConfigCache>(CacheKey);
            if (cache.Help.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        }
        catch { }
        return fallback;
    }

    public static string Notice
    {
        get { try { return Settings.Load<RemoteConfigCache>(CacheKey).Notice; } catch { return ""; } }
    }

    public static string UpdateLog
    {
        get { try { return Settings.Load<RemoteConfigCache>(CacheKey).UpdateLog; } catch { return ""; } }
    }

    public static string MinimumVersion
    {
        get { try { return Settings.Load<RemoteConfigCache>(CacheKey).MinimumVersion; } catch { return ""; } }
    }

    private static string GetStr(JsonElement r, string key)
        => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// 解析 configGet 响应为可读文本（设置页展示）
    public static string ParseResponse(string json)
    {
        try
        {
            Apply(json);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Notice)) parts.Add("公告：" + Notice);
            if (!string.IsNullOrEmpty(UpdateLog)) parts.Add("更新日志：" + UpdateLog);
            if (!string.IsNullOrEmpty(MinimumVersion)) parts.Add("最低版本：" + MinimumVersion);
            if (parts.Count == 0) parts.Add("云端返回空配置");
            return string.Join("\n", parts);
        }
        catch
        {
            return "配置解析失败";
        }
    }
}

/// 云端操作日志（iOS CloudOps.logOp 对齐）
public static class CloudOpsService
{
    public static async Task<bool> LogAsync(string category, string action, string result, string detail = "", string room = "")
    {
        try
        {
            var cfg = CosConfig.Load();
            if (string.IsNullOrEmpty(cfg.FunctionUrl)) return false;
            var resp = await CosService.CallFunctionAsync(cfg, new Dictionary<string, string>
            {
                ["act"] = "logOp",
                ["k"] = RemoteConfigService.PasswordHash(),
                ["u"] = (CosConfig.Load().Username?.Trim() ?? ""),
                ["cat"] = category,
                ["actOp"] = action,
                ["res"] = result,
                ["detail"] = detail,
                ["room"] = room,
            });
            return !string.IsNullOrEmpty(resp);
        }
        catch { return false; }
    }
}
