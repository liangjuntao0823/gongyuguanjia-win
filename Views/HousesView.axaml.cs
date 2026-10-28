using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class HousesView : UserControl
{
    private HousesViewModel? Vm => DataContext as HousesViewModel;

    public HousesView()
    {
        InitializeComponent();
    }

    private void OnToggleLayout(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.DataContext is LayoutChip chip)
            Vm?.ToggleLayout(chip);
    }

    private void OnAddHouse(object? sender, RoutedEventArgs e)
    {
        var dlg = new HouseEditDialog();
        dlg.ShowDialog(GetWindow(this));
    }

    private async void OnSyncMedia(object? sender, RoutedEventArgs e)
    {
        // v1.0：对齐 iOS applySyncPreview——先预览差异，确认后执行
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

    private void OnViewDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is House h)
            OpenDetail(h);
    }

    private void OnViewDetailBtn(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is House h) OpenDetail(h);
    }

    private void OnEditHouseBtn(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is House h)
        {
            var dlg = new HouseEditDialog(h);
            dlg.ShowDialog(GetWindow(this));
        }
    }

    private void OnDeleteHouseBtn(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is House h)
        {
            Vm?.DeleteHouse(h);
            NotifyService.Push("房源删除", $"{h.RoomNo} 已移入回收站", NotifyKind.Info);
        }
    }

    // ---------- v1.0：微信 H5 分享（对齐 iOS：发给客户隐藏版 / 发给同事完整版） ----------

    private async void OnShareMasked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is House h)
            await ShareAsync(h, ShareMode.Masked);
    }

    private async void OnShareFull(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is House h)
            await ShareAsync(h, ShareMode.Full);
    }

    private async Task ShareAsync(House h, ShareMode mode)
    {
        var win = GetWindow(this) ?? GetTopWindow();
        try
        {
            var media = AppDb.GetMedia(h.MediaRoomKey);
            var url = await ShareService.GenerateShareAsync(h, media, mode);
            // 复制链接到剪贴板
            var clipboard = win?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(url);
            NotifyService.Push(mode == ShareMode.Masked ? "分享给客户" : "分享给同事",
                $"链接已生成并复制：{url}", NotifyKind.Success);
            AppDb.AddLog("房源", mode == ShareMode.Masked ? "分享客户" : "分享同事", "成功", $"{h.RoomNo} 链接已复制", h.RoomNo);
        }
        catch (Exception ex)
        {
            NotifyService.Push("分享失败", ex.Message, NotifyKind.Warning);
        }
    }

    // ---------- v1.0：批量导入（扫描 media 目录未收录房号，对齐 iOS 批量录入） ----------

    private async void OnImportHouses(object? sender, RoutedEventArgs e)
    {
        var win = GetWindow(this) ?? GetTopWindow();
        if (win == null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择房源媒体根目录（media）",
            AllowMultiple = false,
        });
        var dir = dirs.FirstOrDefault();
        if (dir == null) return;
        var root = dir.Path.LocalPath;
        if (!Directory.Exists(root)) return;

        var existing = AppDb.GetHouses(true).Select(x => x.MediaRoomKey).ToHashSet();
        var imported = 0;
        var skipped = 0;
        var results = new List<string>();
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var key = Path.GetFileName(sub);
            if (string.IsNullOrEmpty(key) || key.StartsWith("_", StringComparison.Ordinal)) continue;
            if (existing.Contains(key))
            {
                // 已收录 → 补媒体记录（磁盘有而库没有）
                var added = ImportMediaFiles(key);
                if (added > 0) { results.Add($"已收录 {key}：补录媒体 {added} 项"); imported++; }
                else { skipped++; }
                continue;
            }
            var roomNo = key.StartsWith("2-", StringComparison.Ordinal) ? key : key;
            var h = new House
            {
                RoomNo = roomNo,
                Tower = roomNo.StartsWith("2-", StringComparison.Ordinal) ? 2 : 1,
                MediaRoomKey = key,
                Status = "待租",
                IsListed = true,
                LastUpdatedAt = DateTime.Now,
            };
            AppDb.SaveHouse(h);
            var added2 = ImportMediaFiles(key);
            CosService.WriteLocalLedger(h, AppDb.GetMedia(key));
            results.Add($"导入 {key}：房源 + 媒体 {added2} 项");
            imported++;
        }
        AppDb.UpdateHouseHasMediaForAll();
        AppDb.NotifyHousesChanged();
        AppDb.AddLog("房源", "批量导入", "成功", $"导入 {imported} 套，跳过 {skipped} 套");
        NotifyService.Push("批量导入", $"完成：新增 {imported} 套，跳过 {skipped} 套", NotifyKind.Success);
    }

    private static int ImportMediaFiles(string roomKey)
    {
        var dir = DataRoot.RoomMediaDirectory(roomKey);
        if (!Directory.Exists(dir)) return 0;
        var existing = AppDb.GetMedia(roomKey).Select(m => m.FileName).ToHashSet();
        var added = 0;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(f);
            if (name == "info.json" || existing.Contains(name)) continue;
            var lower = name.ToLowerInvariant();
            AppDb.AddMedia(new MediaItem
            {
                RoomKey = roomKey,
                FileName = name,
                Kind = lower.EndsWith(".mp4") || lower.EndsWith(".mov") || lower.EndsWith(".m4v") ? "video" : "image",
                Size = new FileInfo(f).Length,
            });
            added++;
        }
        return added;
    }

    private void OnEditHouse(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is House h)
        {
            var dlg = new HouseEditDialog(h);
            dlg.ShowDialog(GetWindow(this));
        }
    }

    private void OnDeleteHouse(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is House h)
        {
            Vm?.DeleteHouse(h);
            NotifyService.Push("房源删除", $"{h.RoomNo} 已移入回收站", NotifyKind.Info);
        }
    }

    public static void OpenDetail(House h)
    {
        var dlg = new HouseDetailDialog(h);
        dlg.ShowDialog(GetTopWindow());
    }

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null)
        {
            if (c is Window w) return w;
            c = c.Parent;
        }
        return null;
    }

    private static Window? GetTopWindow()
        => Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt
            ? lt.MainWindow
            : null;
}
