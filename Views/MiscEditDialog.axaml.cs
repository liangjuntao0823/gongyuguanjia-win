using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class MiscEditDialog : Window
{
    private readonly bool _isIncome;

    public MiscEditDialog(bool isIncome)
    {
        InitializeComponent();
        _isIncome = isIncome;
        Title = isIncome ? "新增杂项收入" : "新增杂项支出";
        TitleText.Text = Title;
        DatePicker.SelectedDate = DateTime.Today;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var date = DatePicker.SelectedDate?.Date ?? DateTime.Today;
        var amount = AmountBox.Value ?? 0;
        var note = (NoteBox.Text ?? "").Trim();
        if (_isIncome)
        {
            var m = new MiscIncome { Date = date, Amount = amount, Note = note };
            AppDb.SaveMiscIncome(m);
            AppDb.AddLog("成交", "新增杂项收入", "成功", $"{date:yyyy-MM-dd} {amount}元 {note}");
        }
        else
        {
            var m = new MiscExpense { Date = date, Amount = amount, Note = note };
            AppDb.SaveMiscExpense(m);
            AppDb.AddLog("成交", "新增杂项支出", "成功", $"{date:yyyy-MM-dd} {amount}元 {note}");
        }
        AppDb.NotifyDataChanged();
        Close();
    }
}
