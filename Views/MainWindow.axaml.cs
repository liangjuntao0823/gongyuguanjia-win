using System;
using Avalonia.Controls;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => TryAutoBackup();
    }

    // v1.0：启动自动备份（按 iOS 备份频率设置；本地+云端）
    private async void TryAutoBackup()
    {
        try
        {
            var (enabled, intervalDays, keepCount, uploadCloud) = BackupService.GetAutoBackup();
            if (!enabled || !BackupService.ShouldRunAutoBackup()) return;
            var path = BackupService.CreateBackup(true);
            BackupService.CleanupOldBackups(keepCount);
            if (uploadCloud)
            {
                var cfg = CosConfig.Load();
                if (cfg.IsValid)
                    await CloudBackupService.UploadAsync(path, true);
            }
            BackupService.MarkAutoBackupRun();
        }
        catch { }
    }
}
