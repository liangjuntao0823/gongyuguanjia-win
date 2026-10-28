using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 云端备份（v1.0：对齐 iOS v1.40 备份协议）
// - 隔离路径：users/<用户名>/backups/（iOS CloudUser.backupPrefix）
// - 命名：快速_<时间戳>.zip / 全量_<时间戳>.zip
// - 云端保留最近 10 份自动备份（快速_ 前缀）；全量手动上传不自动清理
// - 恢复：下载到本地 → 恢复前自动打包当前数据做安全备份 → 失败自动回滚
// ============================================================

public sealed class CloudBackupEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime Date { get; set; }
    public bool IsFull { get; set; }
}

public static class CloudBackupService
{
    public static string BackupPrefix()
    {
        var u = SyncService.SafeUserName();
        return $"users/{u}/backups/";
    }

    /// 上传本地备份到云端（返回云端对象名）
    public static async Task<string> UploadAsync(string localZipPath, bool full)
    {
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) throw new InvalidOperationException("未配置云端");
        var name = $"{(full ? "全量" : "快速")}_{Path.GetFileNameWithoutExtension(localZipPath).Replace("公寓管家备份-", "").Replace("-", "")}_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var objKey = BackupPrefix() + name;
        var data = await File.ReadAllBytesAsync(localZipPath);
        await CosService.PutObjectAsync(cfg, objKey, data, "application/zip");
        await TrimCloudAsync(cfg, full);
        return objKey;
    }

    /// 列出云端备份（users/<用户名>/backups/）
    public static async Task<List<CloudBackupEntry>> ListCloudAsync()
    {
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) return new List<CloudBackupEntry>();
        var objects = await CosService.ListObjectsAsync(cfg, BackupPrefix());
        var list = new List<CloudBackupEntry>();
        foreach (var o in objects)
        {
            if (string.IsNullOrEmpty(o.Name) || o.Size <= 0) continue;
            var name = o.Name[BackupPrefix().Length..];
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new CloudBackupEntry
            {
                Name = name,
                Size = o.Size,
                Date = o.Date,
                IsFull = name.StartsWith("全量_", StringComparison.Ordinal),
            });
        }
        return list.OrderByDescending(e => e.Date).ToList();
    }

    /// 下载云端备份到本地备份目录
    public static async Task<string> DownloadAsync(string cloudName)
    {
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) throw new InvalidOperationException("未配置云端");
        var objKey = BackupPrefix() + cloudName;
        var data = await CosService.FetchObjectAsync(cfg, objKey);
        var local = Path.Combine(DataRoot.BackupsDirectory, cloudName);
        await File.WriteAllBytesAsync(local, data);
        return local;
    }

    /// 从云端备份恢复：下载 → 恢复前安全备份 → 恢复 → 失败自动回滚
    public static async Task<(bool ok, string msg)> RestoreFromCloudAsync(string cloudName, bool includeMedia)
    {
        try
        {
            var local = await DownloadAsync(cloudName);
            var r = BackupService.Restore(local, includeMedia);
            return (r.ok, r.msg);
        }
        catch (Exception ex)
        {
            return (false, "恢复失败：" + ex.Message);
        }
    }

    /// iOS 语义：云端保留最近 10 份自动备份（快速_ 前缀），全量不清理
    private static async Task TrimCloudAsync(CosConfig cfg, bool full)
    {
        if (full) return;
        try
        {
            var list = await ListCloudAsync();
            var quick = list.Where(e => !e.IsFull).OrderByDescending(e => e.Date).ToList();
            foreach (var old in quick.Skip(10))
                await CosService.DeleteObjectAsync(cfg, BackupPrefix() + old.Name);
        }
        catch { }
    }
}
