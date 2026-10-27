using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class PayoutEditDialog : Window
{
    private readonly PayoutRecord? _editing;

    public PayoutEditDialog(PayoutRecord? editing = null)
    {
        InitializeComponent();
        _editing = editing;
        TitleText.Text = editing == null ? "新增包租合同" : $"编辑包租合同 {editing.RoomNumber}";
        UnitBox.SelectedIndex = 0;
        PayBox.SelectedIndex = 0;
        DueDayBox.Value = 1;
        StartPicker.SelectedDate = DateTime.Today;
        EndPicker.SelectedDate = DateTime.Today.AddYears(1);
        if (editing != null)
        {
            RoomBox.Text = editing.RoomNumber;
            ManagerBox.Text = editing.Manager;
            AnnualBox.Value = editing.AnnualRent;
            MonthlyBox.Value = editing.MonthlyRent;
            DepositBox.Value = editing.Deposit;
            FreeBox.Value = editing.RentFreeDays;
            DueDayBox.Value = editing.RentDueDay;
            DurationBox.Text = editing.LeaseDuration;
            NoteBox.Text = editing.Notes;
            WaterBox.Value = editing.WaterMeterBase;
            ElectricBox.Value = editing.ElectricMeterBase;
            StartPicker.SelectedDate = new DateTimeOffset(editing.LeaseStartDate);
            EndPicker.SelectedDate = new DateTimeOffset(editing.LeaseEndDate);
            SelectItem(UnitBox, editing.UnitType);
            SelectItem(PayBox, editing.PaymentMethod);
        }
    }

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
        var p = _editing ?? new PayoutRecord();
        p.RoomNumber = RoomBox.Text.Trim();
        p.Manager = ManagerBox.Text?.Trim() ?? "";
        p.UnitType = (UnitBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        p.PaymentMethod = (PayBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "月付";
        p.AnnualRent = (int)(AnnualBox.Value ?? 0);
        p.MonthlyRent = MonthlyBox.Value.HasValue ? (int)MonthlyBox.Value.Value : null;
        p.Deposit = (int)(DepositBox.Value ?? 0);
        p.RentFreeDays = (int)(FreeBox.Value ?? 0);
        p.RentDueDay = (int)(DueDayBox.Value ?? 1);
        p.LeaseDuration = DurationBox.Text?.Trim() ?? "";
        p.Notes = NoteBox.Text?.Trim() ?? "";
        p.WaterMeterBase = WaterBox.Value ?? 0;
        p.ElectricMeterBase = ElectricBox.Value ?? 0;
        if (StartPicker.SelectedDate.HasValue) p.LeaseStartDate = StartPicker.SelectedDate.Value.Date;
        if (EndPicker.SelectedDate.HasValue) p.LeaseEndDate = EndPicker.SelectedDate.Value.Date;

        var isNew = p.Id == 0;
        AppDb.SavePayout(p);
        if (isNew) PayoutPlans.EnsureMonthly(p);
        AppDb.AddLog("包租", isNew ? "新增" : "编辑", "成功", $"{p.RoomNumber} {p.Manager} {p.AnnualRent}元/年");
        AppDb.NotifyDataChanged();
        Close();
    }
}
