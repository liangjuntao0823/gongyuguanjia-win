using System.Linq;
using System;
using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class TrashRow
{
    public string Category { get; set; } = "";
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
    public bool IsSelected { get; set; }
}

public class LogRow
{
    public Models.OperationLog Log { get; set; } = new();
    public string Time => Log.Time.ToString("MM-dd HH:mm");
    public string Category => Log.Category;
    public string Action => Log.Action;
    public string Result => Log.Result;
    public string Detail => Log.Detail;
    public string Room => Log.Room;
    public bool IsSelected { get; set; }
}

public class ShareRow
{
    public Models.ShareRecord Share { get; set; } = new();
    public string Time => Share.Time.ToString("yyyy-MM-dd HH:mm");
    public string RoomNo => Share.RoomNo;
    public string Mode => Share.Mode;
}

public class CloudBackupRow
{
    public CloudBackupEntry Entry { get; set; } = new();
    public string Name => Entry.Name;
    public string SizeText => Entry.Size > 1024 * 1024 ? $"{Entry.Size / 1024.0 / 1024.0:0.0} MB" : $"{Entry.Size / 1024.0:0} KB";
    public string DateText => Entry.Date.ToString("yyyy-MM-dd HH:mm");
    public string Kind => Entry.IsFull ? "全量" : "自动";
}

public partial class SettingsViewModel : ViewModelBase
{
    public ObservableCollection<TrashRow> TrashItems { get; } = new();
    public ObservableCollection<LogRow> LogItems { get; } = new();
    public ObservableCollection<ShareRow> ShareItems { get; } = new();
    public ObservableCollection<string> LocalBackups { get; } = new();
    public ObservableCollection<string> CloudBackups { get; } = new();
    public ObservableCollection<string> ThemeOptions { get; } = new() { "白天", "黑夜", "跟随系统" };
    public ObservableCollection<string> PaletteOptions { get; } = new(ThemeManager.Palettes);
    public ObservableCollection<string> WatermarkPositions { get; } = new() { "右下", "左下", "右上", "左上", "居中" };

    [ObservableProperty]
    private string themeMode = Settings.Get("themeMode", "system") switch
    {
        "day" => "白天",
        "night" => "黑夜",
        _ => "跟随系统",
    };
    [ObservableProperty]
    private string themePalette = Settings.Get("themePalette", "默认蓝");
    [ObservableProperty]
    private string backgroundStatus = string.IsNullOrEmpty(Settings.Get("themeBackground", "")) ? "未设置" : "已启用";
    [ObservableProperty]
    private string rentReminderDays = Settings.Get("rentReminderDays", "2");
    [ObservableProperty]
    private string leaseReminderDays = Settings.Get("leaseReminderDays", "30");
    [ObservableProperty]
    private string reminderSound = Settings.Get("reminderSound", "默认提示音");
    [ObservableProperty]
    private string backupIntervalDays = Settings.Get("autoBackupIntervalDays", "7");
    [ObservableProperty]
    private string backupKeepCount = Settings.Get("autoBackupKeepCount", "5");
    [ObservableProperty]
    private bool autoBackupEnabled = Settings.Get("autoBackupEnabled") == "1";
    [ObservableProperty]
    private bool backupUploadCloud = Settings.Get("autoBackupUploadCloud") == "1";

    // 云配置（CosConfig 绑定）
    [ObservableProperty]
    private string cloudUsername = CosConfig.Load().Username;
    [ObservableProperty]
    private string cloudBucket = CosConfig.Load().Bucket;
    [ObservableProperty]
    private string cloudRegion = CosConfig.Load().Region;
    [ObservableProperty]
    private string cloudSecretId = CosConfig.Load().SecretId;
    [ObservableProperty]
    private string cloudSecretKey = CosConfig.Load().SecretKey;
    [ObservableProperty]
    private string cloudCustomDomain = CosConfig.Load().CustomDomain;
    [ObservableProperty]
    private string cloudFunctionUrl = CosConfig.Load().FunctionUrl;
    [ObservableProperty]
    private string adminPassword = CosConfig.Load().AdminPassword;

    // v1.0：分享默认值（对齐 iOS 设置页）
    [ObservableProperty]
    private string shareCardName = Settings.Get("shareCardName", "梁涛");
    [ObservableProperty]
    private string shareCardPhone = Settings.Get("shareCardPhone", "");
    [ObservableProperty]
    private string shareLinkDays = Settings.Get("shareLinkDays", "7");
    [ObservableProperty]
    private string watermarkText = Settings.Get("watermarkText", "利保米兰公馆·梁涛");
    [ObservableProperty]
    private string watermarkPosition = Settings.Get("watermarkPosition", "右下");
    [ObservableProperty]
    private string watermarkOpacity = Settings.Get("watermarkOpacity", "0.35");
    [ObservableProperty]
    private string watermarkFontSize = Settings.Get("watermarkFontSize", "14");
    [ObservableProperty]
    private bool compressUpload = Settings.Get("compressUpload") == "1";

    [ObservableProperty]
    private string remoteConfigText = "未拉取";

    public SettingsViewModel()
    {
        AppDb.DataChanged += RefreshLogs;
        RefreshLogs();
    }

    public void SaveCosConfig()
    {
        var c = new CosConfig
        {
            Username = CloudUsername.Trim(),
            Bucket = CloudBucket.Trim(),
            Region = string.IsNullOrWhiteSpace(CloudRegion) ? "ap-guangzhou" : CloudRegion.Trim(),
            SecretId = CloudSecretId.Trim(),
            SecretKey = CloudSecretKey.Trim(),
            CustomDomain = CloudCustomDomain.Trim(),
            FunctionUrl = CloudFunctionUrl.Trim(),
            AdminPassword = AdminPassword.Trim(),
        };
        c.Save();
        AppDb.AddLog("设置", "云端配置", "保存", $"桶:{c.Bucket} 区域:{c.Region}");
    }

    public void SaveTheme()
    {
        var mode = ThemeMode switch { "白天" => "day", "黑夜" => "night", _ => "system" };
        ThemeManager.SavedMode = mode;
        ThemeManager.SavedPalette = ThemePalette;
        ThemeManager.Apply(mode, ThemePalette);
    }

    public void SaveReminders()
    {
        Settings.Set("rentReminderDays", RentReminderDays);
        Settings.Set("leaseReminderDays", LeaseReminderDays);
        Settings.Set("reminderSound", ReminderSound);
    }

    public void SaveShareDefaults()
    {
        Settings.Set("shareCardName", ShareCardName);
        Settings.Set("shareCardPhone", ShareCardPhone);
        Settings.Set("shareLinkDays", ShareLinkDays);
        AppDb.AddLog("设置", "分享默认值", "保存", $"名片:{ShareCardName} 链接有效期:{ShareLinkDays}天");
    }

    public void SaveWatermark()
    {
        Settings.Set("watermarkText", WatermarkText);
        Settings.Set("watermarkPosition", WatermarkPosition);
        Settings.Set("watermarkOpacity", WatermarkOpacity);
        Settings.Set("watermarkFontSize", WatermarkFontSize);
        AppDb.AddLog("设置", "分享水印", "保存", $"{WatermarkText} {WatermarkPosition} 不透明度{WatermarkOpacity}");
    }

    public void SaveCompress()
        => Settings.Set("compressUpload", CompressUpload ? "1" : "0");

    public void SaveAutoBackup()
    {
        Settings.Set("autoBackupEnabled", AutoBackupEnabled ? "1" : "0");
        Settings.Set("autoBackupIntervalDays", BackupIntervalDays);
        Settings.Set("autoBackupKeepCount", BackupKeepCount);
        Settings.Set("autoBackupUploadCloud", BackupUploadCloud ? "1" : "0");
    }

    public void RefreshTrash()
    {
        TrashItems.Clear();
        foreach (var h in AppDb.GetHouses(true).Where(h => h.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "房源", Id = h.Id, Name = h.RoomNo, DeletedAt = h.DeletedAt });
        foreach (var d in AppDb.GetDeals(true).Where(d => d.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "成交", Id = d.Id, Name = $"{d.RoomNumber} {d.Customer}", DeletedAt = d.DeletedAt });
        foreach (var p in AppDb.GetProperties(true).Where(p => p.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "收租", Id = p.Id, Name = p.RoomNumber, DeletedAt = p.DeletedAt });
        foreach (var p in AppDb.GetPayouts(true).Where(p => p.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "包租", Id = p.Id, Name = p.RoomNumber, DeletedAt = p.DeletedAt });
        foreach (var t in AppDb.GetTodos(true).Where(t => t.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "待办", Id = t.Id, Name = t.Title, DeletedAt = t.DeletedAt });
        foreach (var m in AppDb.GetMiscIncomes(true).Where(m => m.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "杂项收入", Id = m.Id, Name = $"{m.Date:yyyy-MM-dd} {m.Amount}元", DeletedAt = m.DeletedAt });
        foreach (var m in AppDb.GetMiscExpenses(true).Where(m => m.IsDeleted))
            TrashItems.Add(new TrashRow { Category = "杂项支出", Id = m.Id, Name = $"{m.Date:yyyy-MM-dd} {m.Amount}元", DeletedAt = m.DeletedAt });
    }

    public void RefreshLogs()
    {
        LogItems.Clear();
        foreach (var l in AppDb.GetLogs())
            LogItems.Add(new LogRow { Log = l });
    }

    public void RefreshShares()
    {
        ShareItems.Clear();
        foreach (var s in AppDb.GetShareRecords())
            ShareItems.Add(new ShareRow { Share = s });
    }

    public void RefreshBackups()
    {
        LocalBackups.Clear();
        foreach (var b in BackupService.ListLocalBackups())
            LocalBackups.Add(Path.GetFileName(b));
    }

    public async void RefreshCloudBackups()
    {
        CloudBackups.Clear();
        try
        {
            var list = await CloudBackupService.ListCloudAsync();
            foreach (var e in list)
            {
                var sizeText = e.Size > 1024 * 1024 ? $"{e.Size / 1024.0 / 1024.0:0.0} MB" : $"{e.Size / 1024.0:0} KB";
                CloudBackups.Add($"{e.Name} ｜ {sizeText} ｜ {e.Date:yyyy-MM-dd HH:mm} ｜ {(e.IsFull ? "全量" : "自动")}");
            }
        }
        catch (Exception ex)
        {
            CloudBackups.Add("获取失败：" + ex.Message);
        }
    }
}
