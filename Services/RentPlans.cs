using System;
using System.Collections.Generic;
using System.Linq;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

/// <summary>收租/包租月度计划生成（对齐 Mac/iOS 计划编辑器语义）</summary>
public static class RentPlans
{
    /// 按租期生成月度收租记录（每月交租日一期）
    public static void EnsureRecords(Property p)
    {
        var existing = AppDb.GetRentRecords(p.Id);
        if (existing.Count > 0) return;
        var start = DateTime.TryParse(p.LeaseStart, out var s) ? s : DateTime.Today;
        var end = DateTime.TryParse(p.LeaseEnd, out var e) ? e : s.AddYears(1);
        var records = BuildMonthly(start, end, p.Rent, p.RentDueDay);
        foreach (var r in records)
        {
            r.PropertyId = p.Id;
            AppDb.SaveRentRecord(r);
        }
    }

    public static List<RentMonthRecord> BuildMonthly(DateTime start, DateTime end, decimal rent, int dueDay)
    {
        var list = new List<RentMonthRecord>();
        var cursor = new DateTime(start.Year, start.Month, 1);
        var endCursor = new DateTime(end.Year, end.Month, 1);
        while (cursor <= endCursor)
        {
            list.Add(new RentMonthRecord
            {
                Year = cursor.Year,
                Month = cursor.Month,
                Amount = rent,
                IsPaid = false,
            });
            cursor = cursor.AddMonths(1);
        }
        return list;
    }
}

/// <summary>包租打租计划（按付款方式生成周期记录）</summary>
public static class PayoutPlans
{
    public static void EnsureMonthly(PayoutRecord p)
    {
        var existing = AppDb.GetMonthlyPayouts(p.Id);
        if (existing.Count > 0) return;
        var monthly = p.MonthlyRent ?? p.AnnualRent / 12;
        var cursor = new DateTime(p.LeaseStartDate.Year, p.LeaseStartDate.Month, 1);
        var end = new DateTime(p.LeaseEndDate.Year, p.LeaseEndDate.Month, 1);
        var step = p.PaymentMethod switch { "季付" => 3, "半年付" => 6, "年付" => 12, _ => 1 };
        while (cursor <= end)
        {
            AppDb.SaveMonthlyPayout(new MonthlyPayout
            {
                PayoutId = p.Id,
                Month = cursor.Year * 100 + cursor.Month,
                Amount = monthly * step,
                IsPaid = false,
            });
            cursor = cursor.AddMonths(step);
        }
    }
}
