using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
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

    private void OnSyncMedia(object? sender, RoutedEventArgs e)
    {
        Nav.Current?.SyncNowCommand.Execute(null);
    }

    private void OnViewDetail(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is House h)
            OpenDetail(h);
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
