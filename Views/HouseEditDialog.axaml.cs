using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class HouseEditDialog : Window
{
    private readonly House? _editing;

    public HouseEditDialog(House? editing = null)
    {
        InitializeComponent();
        _editing = editing;
        TitleText.Text = editing == null ? "新增房源" : $"编辑房源 {editing.RoomNo}";
        if (editing != null)
        {
            RoomNoBox.Text = editing.RoomNo;
            PriceBox.Value = editing.Price;
            LandlordBox.Text = editing.Landlord;
            DoorBox.Text = editing.DoorPassword;
            RemarkBox.Text = editing.Remark;
            StatusBox.SelectedIndex = editing.IsRented ? 2 : (editing.IsListed ? 1 : 0);
            LayoutBox.SelectedItem = SelectLayoutItem(editing.Layout);
            LeaseStartPicker.SelectedDate = TryParseDate(editing.LeaseStart);
            LeaseEndPicker.SelectedDate = TryParseDate(editing.LeaseEnd);
        }
        else
        {
            StatusBox.SelectedIndex = 0;
            LeaseStartPicker.SelectedDate = DateTime.Today;
            LeaseEndPicker.SelectedDate = DateTime.Today.AddYears(1);
        }
    }

    private ComboBoxItem? SelectLayoutItem(string layout)
    {
        foreach (object? item in LayoutBox.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Content?.ToString() == layout)
                return cbi;
        }
        // 自定义户型：追加
        if (!string.IsNullOrEmpty(layout))
        {
            var custom = new ComboBoxItem { Content = layout };
            LayoutBox.Items.Add(custom);
            return custom;
        }
        return null;
    }

    private static DateTimeOffset? TryParseDate(string s)
        => DateTime.TryParse(s, out var d) ? new DateTimeOffset(d) : null;

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var roomNo = (RoomNoBox.Text ?? "").Trim();
        if (roomNo.Length == 0)
        {
            ErrorText.Text = "请填写房号";
            return;
        }
        // 房号规则：带 "2-" 前缀为 2 座，否则 1 座（用户设定）
        var tower = roomNo.StartsWith("2-", StringComparison.Ordinal) ? 2 : 1;
        var layout = (LayoutBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        var h = _editing ?? new House { MediaRoomKey = roomNo };
        h.RoomNo = roomNo;
        h.Tower = tower;
        h.MediaRoomKey = roomNo;
        h.Layout = layout;
        h.Price = (int)(PriceBox.Value ?? 0);
        h.Landlord = (LandlordBox.Text ?? "").Trim();
        h.DoorPassword = (DoorBox.Text ?? "").Trim();
        h.Remark = (RemarkBox.Text ?? "").Trim();

        var statusIdx = StatusBox.SelectedIndex;
        h.IsRented = statusIdx == 2;
        h.IsListed = statusIdx != 2 && statusIdx != 0; // 0=未上架 1=已上架
        h.Status = h.IsRented ? "已租" : (h.IsListed ? "待租" : "未上架");

        if (LeaseStartPicker.SelectedDate.HasValue)
            h.LeaseStart = LeaseStartPicker.SelectedDate.Value.Date.ToString("yyyy-MM-dd");
        else h.LeaseStart = "";
        if (LeaseEndPicker.SelectedDate.HasValue)
            h.LeaseEnd = LeaseEndPicker.SelectedDate.Value.Date.ToString("yyyy-MM-dd");
        else h.LeaseEnd = "";

        var isNew = h.Id == 0;
        AppDb.SaveHouse(h);
        CosService.WriteLocalLedger(h, AppDb.GetMedia(h.MediaRoomKey));
        AppDb.AddLog("房源", isNew ? "新增" : "编辑", "成功", $"{h.RoomNo} {h.Layout} {h.Price}元 {h.Landlord}", h.RoomNo);
        AppDb.NotifyHousesChanged();
        Close();
    }
}
