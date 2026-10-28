using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class UtilitySettleDialog : Window
{
    private readonly Property _property;

    public UtilitySettleDialog(Property p)
    {
        InitializeComponent();
        _property = p;
        TitleText.Text = $"水电结算 - {p.RoomNumber}";
        WaterBaseBox.Value = p.WaterMeterBase;
        ElectricBaseBox.Value = p.ElectricMeterBase;
        YearBox.Value = DateTime.Now.Year;
        PeriodBox.Value = 1;
        RefreshRecords();
    }

    private void OnSaveBase(object? sender, RoutedEventArgs e)
        => SaveBase();

    private void SaveBase()
    {
        _property.WaterMeterBase = WaterBaseBox.Value ?? 0;
        _property.ElectricMeterBase = ElectricBaseBox.Value ?? 0;
        AppDb.SaveProperty(_property);
        BaseSaveText.Text = "表底数已保存";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        SaveBase();
        var rec = new UtilitySettlementRecord
        {
            PropertyId = _property.Id,
            Year = (int)(YearBox.Value ?? DateTime.Now.Year),
            Period = (int)(PeriodBox.Value ?? 1),
            WaterUsage = WaterUsageBox.Value ?? 0,
            ElectricUsage = ElectricUsageBox.Value ?? 0,
            WaterAmount = WaterAmountBox.Value ?? 0,
            ElectricAmount = ElectricAmountBox.Value ?? 0,
            IsSettled = true,
            SettledDate = DateTime.Now,
        };
        AppDb.SaveUtilityRecord(rec);
        AppDb.AddLog("收租", "水电结算", "成功", $"{_property.RoomNumber} {rec.Year}年第{rec.Period}期 水{rec.WaterAmount}元 电{rec.ElectricAmount}元", _property.RoomNumber);
        AppDb.NotifyDataChanged();
        TotalText.Text = $"本期合计：{rec.WaterAmount + rec.ElectricAmount:0.##} 元（已入结算记录）";
        RefreshRecords();
        NotifyService.Push("水电结算", $"{_property.RoomNumber} {rec.Year}年第{rec.Period}期已保存", NotifyKind.Success);
    }

    private void RefreshRecords()
    {
        var records = AppDb.GetUtilityRecords(_property.Id);
        var items = new List<string>();
        foreach (var r in records)
            items.Add($"{r.Year} 年第 {r.Period} 期：水 {r.WaterUsage:0.##} 吨（{r.WaterAmount:0.##}元）｜ 电 {r.ElectricUsage:0.##} 度（{r.ElectricAmount:0.##}元）｜ {(r.IsSettled ? "已结清" : "未结清")}");
        if (items.Count == 0) items.Add("暂无结算记录");
        RecordList.ItemsSource = items;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
