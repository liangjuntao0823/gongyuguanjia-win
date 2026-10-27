using Avalonia.Platform.Storage;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class DealsView : UserControl
{
    private DealsViewModel? Vm => DataContext as DealsViewModel;

    public DealsView() => InitializeComponent();

    private void OnAddDeal(object? sender, RoutedEventArgs e)
    {
        var dlg = new DealEditDialog();
        dlg.ShowDialog(GetWindow(this));
    }

    private void OnEditDeal(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is DealRecord d)
        {
            var dlg = new DealEditDialog(d);
            dlg.ShowDialog(GetWindow(this));
        }
    }

    private void OnDeleteDeal(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is DealRecord d)
        {
            Vm?.DeleteDeal(d);
            NotifyService.Push("成交删除", $"{d.RoomNumber} 已删除", NotifyKind.Info);
        }
    }

    private void OnExportExcel(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rows = Vm.Rows.Select(r => r.Deal).ToList();
        var path = ExportService.ExportDealsExcel(rows, Vm.SelectedYear, Vm.SelectedMonth == "全部" ? (int?)null : int.Parse(Vm.SelectedMonth.Replace("月", "")));
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private void OnExportImage(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var rows = Vm.Rows.Select(r => (r.Date, r.Summary)).ToList();
        var data = ExportService.RenderRecordsImage($"成交记录 {Vm.SelectedYear}{(Vm.SelectedMonth == "全部" ? "" : Vm.SelectedMonth)}", rows);
        var path = ExportService.SavePng(data, "成交记录");
        NotifyService.Push("导出成功", path, NotifyKind.Success);
    }

    private async void OnImportExcel(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择成交记录 Excel",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } },
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        try
        {
            int count = DealImportService.Import(file.Path.LocalPath);
            NotifyService.Push("导入成功", $"共导入 {count} 条成交记录", NotifyKind.Success);
        }
        catch (Exception ex)
        {
            NotifyService.Push("导入失败", ex.Message, NotifyKind.Warning);
        }
    }

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
}
