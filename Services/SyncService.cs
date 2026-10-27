using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 房源同步（对齐 Mac/iOS 语义）
// - 字段级对比：云端账本与本地 updatedAt「谁新听谁」（时间戳新者覆盖）
// - 媒体双向对账：云端缺→下载；本地多余→查云端对象决定（云端有=删除本地/云端无=删除云端）
// - 磁盘孤儿清理；自动补传缺失云端媒体；本地账本镜像
// ============================================================

public class SyncService
{
    public event Action<string>? StatusChanged;
    public event Action<int>? ProgressChanged;   // 0-100
    public event Action? Completed;

    private bool _running;
    private readonly ConcurrentDictionary<string, string> _tower1Rooms = new();

    public bool IsRunning => _running;

    public string StatusText { get; private set; } = "待同步";

    public async Task<bool> SyncAsync()
    {
        if (_running) return false;
        _running = true;
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

            // 1. 拉取云端房号目录（delimiter 目录式，不列媒体）
            var cloudRooms = await CosService.ListObjectsAsync(cfg, CosService.CloudPrefix, "/");
            var cloudRoomKeys = cloudRooms
                .Select(p => p.TrimEnd('/').Replace(CosService.CloudPrefix, "", StringComparison.Ordinal))
                .Where(p => p.Length > 0 && p.EndsWith("/info.json", StringComparison.OrdinalIgnoreCase) == false)
                .Distinct()
                .ToList();

            // 2. 并发拉取云端账本
            SetStatus("拉取云端账本…");
            var cloudLedgers = new ConcurrentDictionary<string, House>();
            var tasks = cloudRoomKeys.Select(async key =>
            {
                var data = await CosService.GetObjectAsync(cfg, CosService.RecordPath(key));
                if (data != null)
                {
                    try
                    {
                        var h = DeserializeLedger(data, key);
                        if (h != null) cloudLedgers[key] = h;
                    }
                    catch { }
                }
            }).ToArray();
            await Task.WhenAll(tasks);
            ProgressChanged?.Invoke(30);

            // 3. 合并（时间戳新者为准）
            SetStatus("合并字段…");
            var localHouses = AppDb.GetHouses(true);
            var localByRoom = localHouses.Where(h => !string.IsNullOrEmpty(h.MediaRoomKey))
                .GroupBy(h => h.MediaRoomKey).ToDictionary(g => g.Key, g => g.First());

            foreach (var kv in cloudLedgers)
            {
                var cloud = kv.Value;
                var key = kv.Key;
                if (localByRoom.TryGetValue(key, out var local))
                {
                    var changed = FieldsDiffer(local, cloud);
                    if (cloud.UpdatedAt > local.UpdatedAt)
                    {
                        MergeIntoLocal(cloud, local);
                        AppDb.SaveHouse(local);
                    }
                    else if (changed && local.UpdatedAt > cloud.UpdatedAt)
                    {
                        await PushHouseAsync(cfg, local);
                    }
                }
                else
                {
                    // 本地没有：拉取（除非云端已删除）
                    if (!cloud.IsDeleted)
                    {
                        cloud.MediaRoomKey = key;
                        AppDb.SaveHouse(cloud);
                        localByRoom[key] = cloud;
                    }
                }
            }
            // 本地有、云端无：推送本地（新房源/云端被删则重新上传）
            foreach (var l in localHouses.Where(h => !string.IsNullOrEmpty(h.MediaRoomKey)))
            {
                if (!cloudLedgers.ContainsKey(l.MediaRoomKey))
                    await PushHouseAsync(cfg, l);
            }
            ProgressChanged?.Invoke(55);

            // 4. 媒体双向对账（并发）
            SetStatus("对账媒体…");
            await ReconcileMediaAsync(cfg, localByRoom);
            ProgressChanged?.Invoke(85);

            // 5. 同步经营数据（收租/包租/成交/待办 via 云函数，尽力而为）
            SetStatus("同步经营数据…");
            await SyncBusinessDataAsync(cfg);

            SetStatus("同步完成");
            AppDb.NotifyHousesChanged();
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

    /// 字段级对比（两端任一差异即 true）
    public static bool FieldsDiffer(House a, House b)
        => a.RoomNo != b.RoomNo || a.Tower != b.Tower || a.Landlord != b.Landlord || a.Layout != b.Layout ||
           a.Price != b.Price || a.Status != b.Status || a.IsRented != b.IsRented || a.IsListed != b.IsListed ||
           a.Remark != b.Remark || a.DoorPassword != b.DoorPassword || a.LeaseStart != b.LeaseStart ||
           a.LeaseEnd != b.LeaseEnd || a.IsDeleted != b.IsDeleted;

    private static void MergeIntoLocal(House cloud, House local)
    {
        local.RoomNo = cloud.RoomNo; local.Tower = cloud.Tower; local.Landlord = cloud.Landlord;
        local.Layout = cloud.Layout; local.Price = cloud.Price; local.Status = cloud.Status;
        local.IsRented = cloud.IsRented; local.IsListed = cloud.IsListed; local.Remark = cloud.Remark;
        local.DoorPassword = cloud.DoorPassword; local.LeaseStart = cloud.LeaseStart; local.LeaseEnd = cloud.LeaseEnd;
        local.IsDeleted = cloud.IsDeleted; local.UpdatedAt = cloud.UpdatedAt; local.LastUpdatedAt = cloud.LastUpdatedAt;
    }

    private async Task PushHouseAsync(CosConfig cfg, House h)
    {
        var key = h.MediaRoomKey;
        var payload = new Dictionary<string, object?>
        {
            ["roomNo"] = h.RoomNo, ["tower"] = h.Tower, ["landlord"] = h.Landlord, ["layout"] = h.Layout,
            ["price"] = h.Price, ["status"] = h.Status, ["isRented"] = h.IsRented, ["isListed"] = h.IsListed,
            ["remark"] = h.Remark, ["doorPassword"] = h.DoorPassword, ["leaseStart"] = h.LeaseStart,
            ["leaseEnd"] = h.LeaseEnd, ["isDeleted"] = h.IsDeleted,
            ["updatedAt"] = h.UpdatedAt.ToString("o"), ["lastUpdatedAt"] = h.LastUpdatedAt.ToString("o"),
        };
        var data = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload));
        await CosService.PutObjectAsync(cfg, CosService.RecordPath(key), data, "application/json");
        CosService.WriteLocalLedger(h, AppDb.GetMedia(key));
    }

    private static House? DeserializeLedger(byte[] data, string roomKey)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(data);
            var r = doc.RootElement;
            return new House
            {
                RoomNo = r.TryGetProperty("roomNo", out var rn) ? rn.GetString() ?? "" : "",
                Tower = r.TryGetProperty("tower", out var tw) ? tw.GetInt32() : 1,
                Landlord = r.TryGetProperty("landlord", out var ll) ? ll.GetString() ?? "" : "",
                Layout = r.TryGetProperty("layout", out var ly) ? ly.GetString() ?? "" : "",
                Price = r.TryGetProperty("price", out var pr) ? pr.GetInt32() : 0,
                Status = r.TryGetProperty("status", out var st) ? st.GetString() ?? "待租" : "待租",
                IsRented = r.TryGetProperty("isRented", out var ir) && ir.GetBoolean(),
                IsListed = r.TryGetProperty("isListed", out var il) && il.GetBoolean(),
                Remark = r.TryGetProperty("remark", out var rm) ? rm.GetString() ?? "" : "",
                DoorPassword = r.TryGetProperty("doorPassword", out var dp) ? dp.GetString() ?? "" : "",
                LeaseStart = r.TryGetProperty("leaseStart", out var ls) ? ls.GetString() ?? "" : "",
                LeaseEnd = r.TryGetProperty("leaseEnd", out var le) ? le.GetString() ?? "" : "",
                IsDeleted = r.TryGetProperty("isDeleted", out var id) && id.GetBoolean(),
                UpdatedAt = r.TryGetProperty("updatedAt", out var ua) && DateTime.TryParse(ua.GetString(), out var d1) ? d1 : DateTime.Now,
                LastUpdatedAt = r.TryGetProperty("lastUpdatedAt", out var lu) && DateTime.TryParse(lu.GetString(), out var d2) ? d2 : DateTime.Now,
                MediaRoomKey = roomKey,
            };
        }
        catch { return null; }
    }

    private async Task ReconcileMediaAsync(CosConfig cfg, Dictionary<string, House> localByRoom)
    {
        var rooms = AppDb.GetHouses(true).Where(h => !string.IsNullOrEmpty(h.MediaRoomKey)).ToList();
        var tasks = rooms.Select(async h =>
        {
            var key = h.MediaRoomKey;
            // 云端媒体清单
            var cloudKeys = await CosService.ListObjectsAsync(cfg, CosService.CloudPrefix + key + "/");
            var cloudFiles = cloudKeys.Where(k => k.EndsWith("info.json", StringComparison.OrdinalIgnoreCase) == false)
                .Select(k => k.Split('/').Last()).ToHashSet();
            // 本地媒体
            var localMedia = AppDb.GetMedia(key);
            var localFiles = localMedia.Select(m => m.FileName).ToHashSet();
            var roomDir = DataRoot.RoomMediaDirectory(key);

            // 云端有、本地缺 → 下载
            foreach (var f in cloudFiles)
            {
                if (localFiles.Contains(f)) continue;
                var objKey = $"{CosService.CloudPrefix}{key}/{f}";
                var data = await CosService.GetObjectAsync(cfg, objKey);
                if (data != null)
                {
                    File.WriteAllBytes(Path.Combine(roomDir, f), data);
                    AppDb.AddMedia(new MediaItem { RoomKey = key, FileName = f, Kind = f.ToLowerInvariant().EndsWith(".mp4") || f.ToLowerInvariant().EndsWith(".mov") ? "video" : "image", Size = data.Length });
                }
            }
            // 本地有、云端缺 → 补传
            foreach (var f in localFiles)
            {
                if (cloudFiles.Contains(f)) continue;
                var p = Path.Combine(roomDir, f);
                if (File.Exists(p))
                    await CosService.PutObjectAsync(cfg, $"{CosService.CloudPrefix}{key}/{f}", File.ReadAllBytes(p), MimeFor(f));
            }
            // 磁盘孤儿（目录里有文件但库没记录）→ 入库；云端对象但本地无文件且库无记录 → 删除云端
            foreach (var f in Directory.EnumerateFiles(roomDir))
            {
                var name = Path.GetFileName(f);
                if (!localFiles.Contains(name))
                {
                    AppDb.AddMedia(new MediaItem { RoomKey = key, FileName = name, Kind = name.ToLowerInvariant().EndsWith(".mp4") ? "video" : "image", Size = new FileInfo(f).Length });
                }
            }
            var finalLocal = AppDb.GetMedia(key).Select(m => m.FileName).ToHashSet();
            foreach (var f in cloudFiles)
            {
                if (!finalLocal.Contains(f) && !File.Exists(Path.Combine(roomDir, f)))
                    await CosService.DeleteObjectAsync(cfg, $"{CosService.CloudPrefix}{key}/{f}");
            }
        });
        await Task.WhenAll(tasks);
        AppDb.UpdateHouseHasMediaForAll();
    }

    private static string MimeFor(string name)
        => name.ToLowerInvariant().EndsWith(".mp4") || name.ToLowerInvariant().EndsWith(".mov") ? "video/mp4" : "image/jpeg";

    private async Task SyncBusinessDataAsync(CosConfig cfg)
    {
        // 经营数据走云函数存储（尽力而为：首版仅校验连通，后续版本细化）
        try
        {
            if (string.IsNullOrEmpty(cfg.FunctionUrl)) return;
            var resp = await CosService.CallFunctionAsync(cfg, new Dictionary<string, string>
            {
                ["act"] = "configGet", ["u"] = cfg.Username, ["k"] = ""
            });
            if (resp == null) SetStatus("同步完成（经营数据云函数未连通）");
        }
        catch { }
    }

    private void SetStatus(string s)
    {
        StatusText = s;
        StatusChanged?.Invoke(s);
    }
}
