using System.IO;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    public SettingsView() => InitializeComponent();

    // ---------- 云端同步 ----------
    private void OnSaveCos(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveCosConfig();
        NotifyService.Push("云端配置", "已保存", NotifyKind.Success);
    }

    private async void OnTestCos(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.SaveCosConfig();
        var cfg = CosConfig.Load();
        if (!cfg.IsValid) { CosTestResult.Text = "配置不完整（需 Bucket / SecretId / SecretKey）"; return; }
        CosTestResult.Text = "正在测试…";
        try
        {
            var keys = await CosService.ListObjectsAsync(cfg, CosService.CloudPrefix, "/");
            CosTestResult.Text = $"连接成功，云端房号目录 {keys.Count} 个";
            AppDb.AddLog("设置", "云端测试", "成功", $"目录 {keys.Count} 个");
        }
        catch (Exception ex)
        {
            CosTestResult.Text = "连接失败：" + ex.Message;
        }
    }

    private async void OnSyncNow(object? sender, RoutedEventArgs e)
    {
        // v1.0：同步预览确认（对齐 iOS）
        if (Nav.Current is ViewModels.MainViewModel mv && mv.SyncService is { } svc)
        {
            var preview = await svc.BuildPreviewAsync();
            if (preview.Count == 0) { NotifyService.Push("同步", "云端无变化", NotifyKind.Info); return; }
            var win = GetWindow(this);
            var ok = await ConfirmDialog.ShowAsync(win, "同步预览",
                "检测到以下变化：\n\n" + string.Join("\n", preview.Take(30)) +
                (preview.Count > 30 ? $"\n…共 {preview.Count} 项" : "") + "\n\n确认执行同步吗？");
            if (!ok) { NotifyService.Push("同步", "已取消", NotifyKind.Info); return; }
        }
        Nav.Current?.SyncNowCommand.Execute(null);
    }

    // ---------- 分享默认值 / 水印 / 压缩 ----------
    private void OnSaveShareDefaults(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveShareDefaults();
        NotifyService.Push("分享默认值", "已保存（新分享链接生效）", NotifyKind.Success);
    }

    private void OnSaveWatermark(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveWatermark();
        NotifyService.Push("分享水印", "已保存（新分享链接生效）", NotifyKind.Success);
    }

    private void OnSaveCompress(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveCompress();
    }

    // ---------- 远程配置（iOS 对齐：k=SHA256(口令) 鉴权） ----------
    private async void OnFetchRemote(object? sender, RoutedEventArgs e)
    {
        RemoteResult.Text = "正在拉取…";
        var cfg = CosConfig.Load();
        if (string.IsNullOrEmpty(cfg.FunctionUrl)) { RemoteResult.Text = "未配置云函数地址，使用内置默认配置"; return; }
        try
        {
            var resp = await CosService.CallFunctionAsync(cfg, new Dictionary<string, string>
            {
                ["act"] = "configGet",
                ["k"] = RemoteConfigService.PasswordHash(),
                ["u"] = cfg.Username,
            });
            if (string.IsNullOrWhiteSpace(resp))
            {
                RemoteResult.Text = "拉取失败（云函数无响应或鉴权未通过）";
                return;
            }
            var cfgText = RemoteConfigService.ParseResponse(resp);
            Vm!.RemoteConfigText = cfgText;
            RemoteResult.Text = cfgText;
            AppDb.AddLog("设置", "远程配置", "成功", "已拉取云端配置");
        }
        catch (Exception ex)
        {
            RemoteResult.Text = "拉取失败：" + ex.Message;
        }
    }

    // ---------- 备份 ----------
    private void OnQuickBackup(object? sender, RoutedEventArgs e)
    {
        var path = BackupService.CreateBackup(false);
        BackupResult.Text = "快速备份完成：" + path;
        Vm?.RefreshBackups();
    }

    private void OnFullBackup(object? sender, RoutedEventArgs e)
    {
        var path = BackupService.CreateBackup(true);
        BackupResult.Text = "全量备份完成：" + path;
        Vm?.RefreshBackups();
    }

    private void OnSaveAutoBackup(object? sender, RoutedEventArgs e)
        => Vm?.SaveAutoBackup();

    private void OnRefreshBackups(object? sender, RoutedEventArgs e)
        => Vm?.RefreshBackups();

    private async void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        var win = GetWindow(this);
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择备份文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("备份") { Patterns = new[] { "*.zip" } } },
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        var ok = await ConfirmDialog.ShowAsync(win!, "恢复备份", "恢复将覆盖当前数据（恢复前会自动先备份当前数据）。是否继续？");
        if (!ok) return;
        var (success, msg) = BackupService.Restore(file.Path.LocalPath, true);
        BackupResult.Text = msg;
        if (success) NotifyService.Push("恢复完成", "请重启软件查看恢复后的数据", NotifyKind.Success);
    }

    private void OnDeleteBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.LocalBackups.Count == 0) return;
        var name = Vm.LocalBackups[0];
        var path = Path.Combine(DataRoot.BackupsDirectory, name);
        BackupService.DeleteBackup(path);
        Vm.RefreshBackups();
        BackupResult.Text = $"已删除 {name}";
    }

    // v1.0：上传本地备份到云端（users/用户名/backups/，保留最近10份）
    private async void OnUploadBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.LocalBackups.Count == 0) return;
        var name = Vm.LocalBackups[0];
        var path = Path.Combine(DataRoot.BackupsDirectory, name);
        try
        {
            var full = name.Contains("全量", StringComparison.Ordinal);
            var objKey = await CloudBackupService.UploadAsync(path, full);
            BackupResult.Text = $"已上传到云端：{objKey}";
            NotifyService.Push("云端备份", "备份已上传到云端", NotifyKind.Success);
            Vm.RefreshCloudBackups();
        }
        catch (Exception ex)
        {
            BackupResult.Text = "上传失败：" + ex.Message;
            NotifyService.Push("云端备份", "上传失败：" + ex.Message, NotifyKind.Warning);
        }
    }

    // ---------- 云端备份列表 ----------
    private void OnRefreshCloudBackups(object? sender, RoutedEventArgs e)
        => Vm?.RefreshCloudBackups();

    private async void OnDownloadCloudBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.CloudBackups.Count == 0) return;
        var line = Vm.CloudBackups[0];
        var name = line.Split(" ｜")[0];
        try
        {
            var local = await CloudBackupService.DownloadAsync(name);
            BackupResult.Text = $"已下载到本地：{local}";
            NotifyService.Push("云端备份", $"已下载 {name}", NotifyKind.Success);
            Vm.RefreshBackups();
        }
        catch (Exception ex)
        {
            BackupResult.Text = "下载失败：" + ex.Message;
        }
    }

    private async void OnRestoreCloudBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.CloudBackups.Count == 0) return;
        var line = Vm.CloudBackups[0];
        var name = line.Split(" ｜")[0];
        var win = GetWindow(this);
        if (win == null) return;
        var ok = await ConfirmDialog.ShowAsync(win, "从云端恢复", $"将从云端恢复备份 {name}，恢复前会自动备份当前数据。是否继续？");
        if (!ok) return;
        try
        {
            var (success, msg) = await CloudBackupService.RestoreFromCloudAsync(name, true);
            BackupResult.Text = msg;
            if (success) NotifyService.Push("恢复完成", "请重启软件查看恢复后的数据", NotifyKind.Success);
        }
        catch (Exception ex)
        {
            BackupResult.Text = "恢复失败：" + ex.Message;
        }
    }

    // ---------- 回收站 ----------
    private void OnRefreshTrash(object? sender, RoutedEventArgs e)
        => Vm?.RefreshTrash();

    private async void OnRestoreTrash(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var sel = Vm.TrashItems.Where(t => t.IsSelected).ToList();
        if (sel.Count == 0) { NotifyService.Push("提示", "请先勾选要恢复的项目", NotifyKind.Warning); return; }
        foreach (var row in sel)
        {
            switch (row.Category)
            {
                case "房源": AppDb.RestoreHouse(row.Id); break;
                case "成交": AppDb.RestoreDeal(row.Id); break;
                case "收租": AppDb.RestoreProperty(row.Id); break;
                case "包租": AppDb.RestorePayout(row.Id); break;
                case "待办": AppDb.RestoreTodo(row.Id); break;
                case "杂项收入": AppDb.RestoreMiscIncome(row.Id); break;
                case "杂项支出": AppDb.RestoreMiscExpense(row.Id); break;
            }
        }
        AppDb.NotifyDataChanged();
        AppDb.NotifyHousesChanged();
        Vm.RefreshTrash();
        NotifyService.Push("回收站", $"已恢复 {sel.Count} 项", NotifyKind.Success);
    }

    private async void OnPurgeTrash(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var sel = Vm.TrashItems.Where(t => t.IsSelected).ToList();
        if (sel.Count == 0) { NotifyService.Push("提示", "请先勾选要删除的项目", NotifyKind.Warning); return; }
        var ok = await ConfirmDialog.ShowAsync(GetWindow(this)!, "彻底删除", $"确定彻底删除 {sel.Count} 项？此操作不可恢复。");
        if (!ok) return;
        foreach (var row in sel)
        {
            switch (row.Category)
            {
                case "房源": AppDb.PermanentDeleteHouse(row.Id); break;
                case "成交": AppDb.PermanentDeleteDeal(row.Id); break;
                case "收租": AppDb.PermanentDeleteProperty(row.Id); break;
                case "包租": AppDb.PermanentDeletePayout(row.Id); break;
                case "待办": AppDb.PermanentDeleteTodo(row.Id); break;
                case "杂项收入": AppDb.PermanentDeleteMiscIncome(row.Id); break;
                case "杂项支出": AppDb.PermanentDeleteMiscExpense(row.Id); break;
            }
        }
        AppDb.NotifyDataChanged();
        AppDb.NotifyHousesChanged();
        Vm.RefreshTrash();
        NotifyService.Push("回收站", $"已彻底删除 {sel.Count} 项", NotifyKind.Success);
    }

    // v1.0：按板块清空（对齐 iOS 回收站分类清空）
    private async void OnPurgeByCategory(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || sender is not Button b || b.CommandParameter is not string category) return;
        var rows = Vm.TrashItems.Where(t => t.Category == category).ToList();
        if (rows.Count == 0) { NotifyService.Push("回收站", $"{category}板块无待清空项目", NotifyKind.Info); return; }
        var ok = await ConfirmDialog.ShowAsync(GetWindow(this)!, "按板块清空", $"确定彻底清空 {category} 板块 {rows.Count} 项？此操作不可恢复。");
        if (!ok) return;
        foreach (var row in rows)
        {
            switch (row.Category)
            {
                case "房源": AppDb.PermanentDeleteHouse(row.Id); break;
                case "成交": AppDb.PermanentDeleteDeal(row.Id); break;
                case "收租": AppDb.PermanentDeleteProperty(row.Id); break;
                case "包租": AppDb.PermanentDeletePayout(row.Id); break;
                case "待办": AppDb.PermanentDeleteTodo(row.Id); break;
                case "杂项收入": AppDb.PermanentDeleteMiscIncome(row.Id); break;
                case "杂项支出": AppDb.PermanentDeleteMiscExpense(row.Id); break;
            }
        }
        AppDb.NotifyDataChanged();
        AppDb.NotifyHousesChanged();
        Vm.RefreshTrash();
        NotifyService.Push("回收站", $"已清空 {category} {rows.Count} 项", NotifyKind.Success);
    }

    // ---------- 外观与提醒 ----------
    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
        => Vm?.SaveTheme();

    private async void OnPickBackground(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择背景图片",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" } } },
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        var ok = ThemeManager.SetBackgroundImage(file.Path.LocalPath);
        if (!ok) { NotifyService.Push("背景", "图片加载失败", NotifyKind.Warning); return; }
        Vm!.BackgroundStatus = "已启用";
        NotifyService.Push("背景", "自定义背景已生效", NotifyKind.Success);
    }

    private void OnClearBackground(object? sender, RoutedEventArgs e)
    {
        ThemeManager.ClearBackground();
        Vm!.BackgroundStatus = "未设置";
        NotifyService.Push("背景", "已恢复默认背景", NotifyKind.Info);
    }

    private void OnSaveReminders(object? sender, RoutedEventArgs e)
    {
        Vm?.SaveReminders();
        NotifyService.Push("提醒设置", "已保存", NotifyKind.Success);
    }

    // ---------- 日志与分享 ----------
    private void OnRefreshLogs(object? sender, RoutedEventArgs e)
        => Vm?.RefreshLogs();

    private void OnDeleteLogs(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var ids = Vm.LogItems.Where(l => l.IsSelected).Select(l => l.Log.Id).ToList();
        if (ids.Count == 0) return;
        AppDb.DeleteLogs(ids);
        Vm.RefreshLogs();
        NotifyService.Push("操作日志", $"已删除 {ids.Count} 条", NotifyKind.Success);
    }

    // v1.0：上传云端日志（对齐 iOS CloudOps act=logOp）
    private async void OnUploadLogs(object? sender, RoutedEventArgs e)
    {
        var logs = AppDb.GetLogs().Take(20).ToList();
        if (logs.Count == 0) { NotifyService.Push("日志", "暂无日志可上传", NotifyKind.Info); return; }
        var ok = 0;
        foreach (var l in logs)
        {
            if (await CloudOpsService.LogAsync(l.Category, l.Action, l.Result, l.Detail, l.Room)) ok++;
        }
        NotifyService.Push("云端日志", $"已上传 {ok}/{logs.Count} 条", ok > 0 ? NotifyKind.Success : NotifyKind.Warning);
    }

    private void OnRefreshShares(object? sender, RoutedEventArgs e)
        => Vm?.RefreshShares();

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
    private void OnRefreshCloudShares(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm) vm.RefreshCloudShares();
    }

}
