using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Views;

public partial class PayoutPackageEditDialog : UserControl
{
    public PayoutPackageRecord? Result { get; private set; }
    private readonly PayoutPackageRecord? _source;
    private readonly Action? _onSaved;

    public PayoutPackageEditDialog() : this(null, null) { }

    public PayoutPackageEditDialog(PayoutPackageRecord? source, Action? onSaved)
    {
        InitializeComponent();
        _source = source;
        _onSaved = onSaved;
        if (source != null)
        {
            RoomNoBox.Text = source.RoomNumber;
            TotalAmountBox.Text = source.TotalPackageAmount.ToString("0.##");
            PackageStartPicker.SelectedDate = source.PackageStart;
            PackageEndPicker.SelectedDate = source.PackageEnd;
            VacancyDaysBox.Text = source.VacancyDays.ToString();
            TenantRentBox.Text = source.TenantMonthlyRent.ToString("0.##");
            RentStartPicker.SelectedDate = source.RentStart;
            RentEndPicker.SelectedDate = source.RentEnd;
            NotesBox.Text = source.Notes;
        }
        else
        {
            PackageStartPicker.SelectedDate = DateTime.Today;
            PackageEndPicker.SelectedDate = DateTime.Today.AddDays(365);
            RentStartPicker.SelectedDate = DateTime.Today;
            RentEndPicker.SelectedDate = DateTime.Today.AddDays(365);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (this.FindParentWindow() is Window w) w.Close();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(RoomNoBox.Text)) { ShowTip("请填写房号"); return; }
            var rec = _source ?? new PayoutPackageRecord();
            rec.RoomNumber = RoomNoBox.Text.Trim();
            rec.TotalPackageAmount = decimal.TryParse(TotalAmountBox.Text, out var ta) ? ta : 0;
            rec.PackageStart = PackageStartPicker.SelectedDate?.DateTime ?? DateTime.Today;
            rec.PackageEnd = PackageEndPicker.SelectedDate?.DateTime ?? DateTime.Today;
            rec.VacancyDays = int.TryParse(VacancyDaysBox.Text, out var vd) ? vd : 0;
            rec.TenantMonthlyRent = decimal.TryParse(TenantRentBox.Text, out var tr) ? tr : 0;
            rec.RentStart = RentStartPicker.SelectedDate?.DateTime ?? DateTime.Today;
            rec.RentEnd = RentEndPicker.SelectedDate?.DateTime ?? DateTime.Today;
            rec.Notes = NotesBox.Text ?? "";
            Result = rec;
            _onSaved?.Invoke();
            if (this.FindParentWindow() is Window w) w.Close();
        }
        catch (Exception ex)
        {
            ShowTip("保存失败：" + ex.Message);
        }
    }

    private void ShowTip(string msg)
    {
        if (ErrBox != null) ErrBox.Text = msg;
    }
}

public static class ControlExtensions2
{
    public static Window? FindParentWindow(this Control c)
    {
        var p = c.Parent;
        while (p != null) { if (p is Window win) return win; p = p.Parent; }
        return null;
    }
}
