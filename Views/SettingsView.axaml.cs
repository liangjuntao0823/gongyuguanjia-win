using System.IO;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
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

    private void OnSyncNow(object? sender, RoutedEventArgs e)
    {
        Nav.Current?.SyncNowCommand.Execute(null);
    }

    private async void OnFetchRemote(object? sender, RoutedEventArgs e)
    {
        RemoteResult.Text = "正在拉取…";
        var cfg = CosConfig.Load();
        if (string.IsNullOrEmpty(cfg.FunctionUrl)) { RemoteResult.Text = "未配置云函数地址，使用内置默认配置"; return; }
        var resp = await CosService.CallFunctionAsync(cfg, new Dictionary<string, string> { ["act"] = "configGet", ["u"] = cfg.Username });
        RemoteResult.Text = resp == null ? "拉取失败（云函数无响应）" : "远程配置已拉取：" + (resp.Length > 300 ? resp[..300] + "…" : resp);
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

    private async void OnUploadBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.LocalBackups.Count == 0) return;
        var name = Vm.LocalBackups[0];
        var path = Path.Combine(DataRoot.BackupsDirectory, name);
        var ok = await BackupService.UploadToCloudAsync(path);
        BackupResult.Text = ok ? "已上传到云端" : "上传失败（云端未配置或网络错误）";
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
            }
        }
        AppDb.NotifyDataChanged();
        AppDb.NotifyHousesChanged();
        Vm.RefreshTrash();
        NotifyService.Push("回收站", $"已彻底删除 {sel.Count} 项", NotifyKind.Success);
    }

    // ---------- 外观与提醒 ----------
    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
        => Vm?.SaveTheme();

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

    private void OnRefreshShares(object? sender, RoutedEventArgs e)
        => Vm?.RefreshShares();

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
}
