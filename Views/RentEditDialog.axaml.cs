using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class RentEditDialog : Window
{
    private readonly Property? _editing;

    public RentEditDialog(Property? editing = null)
    {
        InitializeComponent();
        _editing = editing;
        TitleText.Text = editing == null ? "新增收租房源" : $"编辑收租房源 {editing.RoomNumber}";
        TypeBox.SelectedIndex = 0;
        UnitBox.SelectedIndex = 0;
        DueDayBox.Value = 1;
        StartPicker.SelectedDate = DateTime.Today;
        EndPicker.SelectedDate = DateTime.Today.AddYears(1);
        if (editing != null)
        {
            RoomBox.Text = editing.RoomNumber;
            LandlordBox.Text = editing.Landlord;
            RentBox.Value = editing.Rent;
            DepositBox.Value = editing.Deposit;
            PrepayBox.Value = editing.Prepayment;
            DueDayBox.Value = editing.RentDueDay;
            NoteBox.Text = editing.Notes;
            WaterBox.Value = editing.WaterMeterBase;
            ElectricBox.Value = editing.ElectricMeterBase;
            SelectItem(TypeBox, editing.Type);
            SelectItem(UnitBox, editing.UnitType);
            StartPicker.SelectedDate = TryParse(editing.LeaseStart);
            EndPicker.SelectedDate = TryParse(editing.LeaseEnd);
        }
    }

    private static DateTimeOffset? TryParse(string s) => DateTime.TryParse(s, out var d) ? new DateTimeOffset(d) : null;

    private static void SelectItem(ComboBox box, string value)
    {
        for (int i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem cbi && cbi.Content?.ToString() == value) { box.SelectedIndex = i; return; }
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RoomBox.Text)) { ErrorText.Text = "请填写房号"; return; }
        var p = _editing ?? new Property();
        p.RoomNumber = RoomBox.Text.Trim();
        p.Landlord = LandlordBox.Text?.Trim() ?? "";
        p.UnitType = (UnitBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        p.Type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "管理";
        p.Rent = (int)(RentBox.Value ?? 0);
        p.Deposit = (int)(DepositBox.Value ?? 0);
        p.Prepayment = (int)(PrepayBox.Value ?? 0);
        p.RentDueDay = (int)(DueDayBox.Value ?? 1);
        p.Notes = NoteBox.Text?.Trim() ?? "";
        p.WaterMeterBase = WaterBox.Value ?? 0;
        p.ElectricMeterBase = ElectricBox.Value ?? 0;
        p.LeaseStart = StartPicker.SelectedDate?.Date.ToString("yyyy-MM-dd") ?? "";
        p.LeaseEnd = EndPicker.SelectedDate?.Date.ToString("yyyy-MM-dd") ?? "";
        var isNew = p.Id == 0;
        AppDb.SaveProperty(p);
        // 初始化月度收租记录（补齐到租期结束）
        if (isNew) RentPlans.EnsureRecords(p);
        AppDb.AddLog("收租", isNew ? "新增" : "编辑", "成功", $"{p.RoomNumber} {p.Rent}元/月 每{p.RentDueDay}日");
        AppDb.NotifyDataChanged();
        Close();
    }
}
