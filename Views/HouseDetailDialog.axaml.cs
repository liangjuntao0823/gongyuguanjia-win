using Avalonia.Platform.Storage;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class HouseDetailDialog : Window
{
    private readonly House _house;
    private readonly List<MediaItem> _media = new();

    public HouseDetailDialog(House h)
    {
        InitializeComponent();
        _house = h;
        RoomNoText.Text = h.RoomNo;
        LayoutText.Text = string.IsNullOrEmpty(h.Layout) ? "户型：-" : $"户型：{h.Layout}";
        LandlordText.Text = string.IsNullOrEmpty(h.Landlord) ? "" : $"房东：{h.Landlord}";
        PriceText.Text = h.Price > 0 ? $"{h.Price} 元/月" : "";
        LeaseText.Text = !string.IsNullOrEmpty(h.LeaseStart) ? $"租期：{h.LeaseStart} ~ {h.LeaseEnd}" : "";
        RemarkText.Text = string.IsNullOrEmpty(h.Remark) ? "" : $"备注：{h.Remark}";
        StatusText.Text = h.IsRented ? "已租" : (h.IsListed ? "待租" : "未上架");
        // 双端标识：未配置云端时只显示「本地」
        var cfg = CosConfig.Load();
        var badge = !cfg.IsValid ? "本地（未配置云端）" : h.CloudStatus switch
        {
            "both" => "双端",
            "cloud" => "云端",
            _ => "本地",
        };
        CloudBadgeText.Text = badge;
        LoadMedia();
    }

    private void LoadMedia()
    {
        MediaPanel.Children.Clear();
        _media.Clear();
        var dir = DataRoot.RoomMediaDirectory(_house.MediaRoomKey);
        if (!Directory.Exists(dir)) return;
        foreach (var item in AppDb.GetMedia(_house.MediaRoomKey))
            _media.Add(item);

        if (_media.Count == 0)
        {
            MediaPanel.Children.Add(new TextBlock { Text = "暂无媒体", Margin = new Thickness(0, 20, 0, 0), FontSize = 14 });
            return;
        }

        foreach (var m in _media)
        {
            var path = Path.Combine(dir, m.FileName);
            if (!File.Exists(path)) continue;

            var border = new Border
            {
                Width = 200,
                Height = 150,
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(Color.Parse("#000000")),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
            };

            var grid = new Grid();
            if (m.Kind == "video")
            {
                var thumb = ExportService.VideoFirstFrame(path, 400);
                if (thumb != null)
                {
                    try { grid.Children.Add(new Image { Source = new Bitmap(new MemoryStream(thumb)), Stretch = Stretch.UniformToFill }); }
                    catch { AddVideoPlaceholder(grid); }
                }
                else AddVideoPlaceholder(grid);
                // 大播放按钮（详情页便于一眼区分视频）
                var play = new Button
                {
                    Content = "▶",
                    FontSize = 26,
                    Width = 56,
                    Height = 56,
                    CornerRadius = new CornerRadius(28),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                play.Click += (_, _) => OpenVideo(path);
                grid.Children.Add(play);
                var fileLabel = new TextBlock
                {
                    Text = "视频",
                    FontSize = 11,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(8, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                };
                grid.Children.Add(fileLabel);
            }
            else
            {
                // 图片：缩略图
                try
                {
                    var thumb = ExportService.MakeThumbnail(path, 400) ?? File.ReadAllBytes(path);
                    var img = new Image { Source = new Bitmap(new MemoryStream(thumb)), Stretch = Stretch.UniformToFill };
                    img.PointerPressed += (_, _) => OpenImage(path);
                    grid.Children.Add(img);
                }
                catch
                {
                    grid.Children.Add(new TextBlock { Text = m.FileName, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                }
            }

            border.Child = grid;
            MediaPanel.Children.Add(border);
        }
    }

    private static void AddVideoPlaceholder(Grid grid)
    {
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A2D35")),
            Child = new TextBlock { Text = "视频", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 18 },
        });
    }

    private static void OpenVideo(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { NotifyService.Push("打开失败", ex.Message); }
    }

    private void OpenImage(string path)
    {
        try
        {
            var dlg = new Window
            {
                Title = "图片查看",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.Parse("#1A1A1A")),
            };
            var img = new Image { Source = new Bitmap(path), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            dlg.Content = img;
            dlg.ShowDialog(this);
        }
        catch { }
    }

    private async void OnSaveAllMedia(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择保存目录" });
        var picked = dirs.FirstOrDefault();
        if (picked == null) return;
        var dir = picked.Path.LocalPath;
        var dst = Path.Combine(dir, _house.RoomNo + "_媒体");
        Directory.CreateDirectory(dst);
        int count = 0;
        foreach (var m in _media)
        {
            var src = Path.Combine(DataRoot.RoomMediaDirectory(_house.MediaRoomKey), m.FileName);
            if (File.Exists(src)) { File.Copy(src, Path.Combine(dst, m.FileName), true); count++; }
        }
        NotifyService.Push("保存完成", $"已保存 {count} 个媒体文件到 {dst}", NotifyKind.Success);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
