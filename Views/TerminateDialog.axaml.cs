using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class TerminateDialog : Window
{
    private readonly Property _property;

    public TerminateDialog(Property p)
    {
        InitializeComponent();
        _property = p;
        TitleText.Text = $"退租登记 - {p.RoomNumber}";
        TermDatePicker.SelectedDate = DateTime.Today;
        ReasonBox.SelectedIndex = 0;
        DepositBox.SelectedIndex = 0;
        UtilityBox.SelectedIndex = 0;
        if (p.IsTerminated)
        {
            if (p.TerminationDate.HasValue) TermDatePicker.SelectedDate = p.TerminationDate.Value;
            SelectText(ReasonBox, p.TerminationReason);
            SelectText(DepositBox, p.DepositRefundStatus);
            SelectText(UtilityBox, p.UtilitySettlementStatus);
            DamageBox.Text = p.RoomDamageStatus;
            PersonBox.Text = p.TerminationPerson;
            PhoneBox.Text = p.TerminationPhone;
        }
    }

    private static void SelectText(ComboBox box, string text)
    {
        for (int i = 0; i < box.ItemCount; i++)
        {
            if (box.Items[i] is ComboBoxItem cbi && cbi.Content?.ToString() == text) { box.SelectedIndex = i; return; }
        }
    }

    private static string Selected(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _property.IsTerminated = true;
        _property.TerminationDate = TermDatePicker.SelectedDate?.Date ?? DateTime.Today;
        _property.TerminationReason = Selected(ReasonBox);
        _property.DepositRefundStatus = Selected(DepositBox);
        _property.RoomDamageStatus = (DamageBox.Text ?? "").Trim();
        _property.UtilitySettlementStatus = Selected(UtilityBox);
        _property.TerminationPerson = (PersonBox.Text ?? "").Trim();
        _property.TerminationPhone = (PhoneBox.Text ?? "").Trim();
        AppDb.SaveProperty(_property);
        AppDb.AddLog("收租", "退租", "成功", $"{_property.RoomNumber} {_property.TerminationReason} {_property.TerminationDate:yyyy-MM-dd}", _property.RoomNumber);
        AppDb.NotifyDataChanged();
        NotifyService.Push("退租登记", $"{_property.RoomNumber} 已登记退租", NotifyKind.Success);
        Close();
    }
}
