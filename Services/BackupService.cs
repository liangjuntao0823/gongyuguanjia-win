using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 备份与恢复（对齐 Mac/iOS：快速/全量、自动备份、ZipStore、云端备份）
// ============================================================

public static class BackupService
{
    public static string BackupFile(bool full)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(DataRoot.BackupsDirectory, $"公寓管家备份-{(full ? "全量" : "快速")}-{stamp}.zip");
    }

    /// 快速备份：数据库 + 设置 + 房源账本镜像；全量备份另含 media 目录
    public static string CreateBackup(bool full)
    {
        var path = BackupFile(full);
        Directory.CreateDirectory(DataRoot.BackupsDirectory);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(DataRoot.DbPath, "apartment.db");
        var settings = Path.Combine(DataRoot.Directory, "settings.json");
        if (File.Exists(settings))
            zip.CreateEntryFromFile(settings, "settings.json");
        if (full && Directory.Exists(DataRoot.MediaDirectory))
        {
            foreach (var f in Directory.EnumerateFiles(DataRoot.MediaDirectory, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(DataRoot.Directory, f);
                zip.CreateEntryFromFile(f, rel);
            }
        }
        AppDb.AddLog("备份", full ? "全量备份" : "快速备份", "成功", path);
        return path;
    }

    public static List<string> ListLocalBackups()
        => Directory.Exists(DataRoot.BackupsDirectory)
            ? Directory.EnumerateFiles(DataRoot.BackupsDirectory, "*.zip").OrderByDescending(f => f).ToList()
            : new List<string>();

    /// 按保留份数清理本地旧备份（最新保留，仅删 zip）
    public static void CleanupOldBackups(int keepCount)
    {
        try
        {
            if (keepCount <= 0) return;
            var files = Directory.GetFiles(DataRoot.BackupsDirectory, "*.zip")
                .OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
            foreach (var f in files.Skip(keepCount))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public static (bool ok, string msg) Restore(string zipPath, bool includeMedia)
    {
        // 恢复前自动打包当前数据做安全备份（iOS 语义）；失败自动回滚
        string safety;
        try { safety = CreateBackup(false); }
        catch { safety = ""; }
        try
        {
            var r = RestoreCore(zipPath, includeMedia);
            if (!r.ok && !string.IsNullOrEmpty(safety)) TryRollback(safety);
            return r;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(safety)) TryRollback(safety);
            return (false, "恢复失败：" + ex.Message);
        }
    }

    private static (bool ok, string msg) RestoreCore(string zipPath, bool includeMedia)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            // 校验合法性
            var dbEntry = zip.GetEntry("apartment.db");
            if (dbEntry == null) return (false, "备份文件缺少 apartment.db，无法恢复");

            var tmp = DataRoot.Directory + ".restore_tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                var target = Path.Combine(tmp, e.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }

            // 替换
            File.Copy(Path.Combine(tmp, "apartment.db"), DataRoot.DbPath, true);
            if (File.Exists(Path.Combine(tmp, "settings.json")))
                File.Copy(Path.Combine(tmp, "settings.json"), Path.Combine(DataRoot.Directory, "settings.json"), true);
            if (includeMedia && Directory.Exists(Path.Combine(tmp, "media")))
            {
                if (Directory.Exists(DataRoot.MediaDirectory))
                    Directory.Delete(DataRoot.MediaDirectory, true);
                Directory.Move(Path.Combine(tmp, "media"), DataRoot.MediaDirectory);
            }
            Directory.Delete(tmp, true);
            AppDb.AddLog("恢复", "从备份恢复", "成功", zipPath);
            return (true, "恢复成功");
        }
        catch (Exception ex)
        {
            return (false, "恢复失败：" + ex.Message);
        }
    }

    private static void TryRollback(string safetyZip)
    {
        try
        {
            using var zip = ZipFile.OpenRead(safetyZip);
            var dbEntry = zip.GetEntry("apartment.db");
            if (dbEntry == null) return;
            dbEntry.ExtractToFile(DataRoot.DbPath, true);
        }
        catch { }
    }

    public static void DeleteBackup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// 自动备份配置（v1.0：对齐 iOS——每天/每周/每月 + 保留天数，默认 7 天）
    public static (bool enabled, string frequency, int retentionDays, bool uploadCloud) GetAutoBackup()
    {
        return (
            Settings.Get("autoBackupEnabled") == "1",
            Settings.Get("autoBackupFrequency", "day"),
            int.TryParse(Settings.Get("autoBackupRetentionDays"), out var r) ? r : 7,
            Settings.Get("autoBackupUploadCloud") == "1");
    }

    public static void SetAutoBackup(bool enabled, string frequency, int retentionDays, bool uploadCloud)
    {
        Settings.Set("autoBackupEnabled", enabled ? "1" : "0");
        Settings.Set("autoBackupFrequency", frequency);
        Settings.Set("autoBackupRetentionDays", retentionDays.ToString());
        Settings.Set("autoBackupUploadCloud", uploadCloud ? "1" : "0");
    }

    public static bool ShouldRunAutoBackup()
    {
        var (enabled, frequency, _, _) = GetAutoBackup();
        if (!enabled) return false;
        var gap = frequency switch { "week" => 7, "month" => 30, _ => 1 };
        var last = Settings.Get("autoBackupLastRun", "");
        if (!DateTime.TryParse(last, out var lastTime)) return true;
        return (DateTime.Now - lastTime).TotalDays >= gap;
    }

    /// v1.0：按保留天数清理本地旧备份（对齐 iOS backupRetentionDays）
    public static void CleanupOldBackupsByDays(int retentionDays)
    {
        try
        {
            var dir = DataRoot.Directory;
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "备份_*.zip").OrderByDescending(f => File.GetLastWriteTime(f)).ToList();
            foreach (var f in files)
            {
                if ((DateTime.Now - File.GetLastWriteTime(f)).TotalDays > retentionDays)
                { try { File.Delete(f); } catch { } }
            }
        }
        catch { }
    }

    public static void MarkAutoBackupRun()
        => Settings.Set("autoBackupLastRun", DateTime.Now.ToString("o"));

    /// 云端备份（上传到 COS backup/<file>）
    public static async Task<bool> UploadToCloudAsync(string path)
    {
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) return false;
        var key = "backup/" + Path.GetFileName(path);
        var ok = await CosService.PutObjectAsync(cfg, key, File.ReadAllBytes(path), "application/zip");
        if (ok) AppDb.AddLog("备份", "云端备份上传", "成功", key);
        return ok;
    }
}
