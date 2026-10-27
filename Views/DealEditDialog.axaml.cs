using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class DealEditDialog : Window
{
    private readonly DealRecord? _editing;

    public DealEditDialog(DealRecord? editing = null)
    {
        InitializeComponent();
        _editing = editing;
        TitleText.Text = editing == null ? "新增成交" : "编辑成交";
        TypeBox.SelectedIndex = 0;
        DatePicker.SelectedDate = DateTime.Today;
        if (editing != null)
        {
            DatePicker.SelectedDate = editing.Date;
            RoomBox.Text = editing.RoomNumber;
            CustomerBox.Text = editing.Customer;
            AmountBox.Value = editing.Amount;
            FeeBox.Value = editing.AgentFee;
            NoteBox.Text = editing.Note;
            SelectType(editing.Type);
        }
    }

    private void SelectType(string type)
    {
        for (int i = 0; i < TypeBox.ItemCount; i++)
        {
            if (TypeBox.Items[i] is ComboBoxItem cbi && cbi.Content?.ToString() == type) { TypeBox.SelectedIndex = i; return; }
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var d = _editing ?? new DealRecord();
        d.Date = DatePicker.SelectedDate?.Date ?? DateTime.Today;
        d.RoomNumber = (RoomBox.Text ?? "").Trim();
        d.Customer = (CustomerBox.Text ?? "").Trim();
        d.Type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "收租成交";
        d.Amount = AmountBox.Value ?? 0;
        d.AgentFee = FeeBox.Value ?? 0;
        d.Note = (NoteBox.Text ?? "").Trim();
        var isNew = d.Id == 0;
        AppDb.SaveDeal(d);
        AppDb.AddLog("成交", isNew ? "新增" : "编辑", "成功", $"{d.RoomNumber} {d.Type} {d.Amount}元");
        AppDb.NotifyDataChanged();
        Close();
    }
}
