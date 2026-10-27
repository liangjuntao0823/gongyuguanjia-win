using System;
using System.Collections.Generic;
using System.Linq;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 应用内通知（横幅）+ 提醒检查（收租/租期/包租/待办）
// ============================================================

public static class NotifyService
{
    public static event Action<NotifyItem>? NotifyRaised;

    public static void Push(string title, string message, NotifyKind kind = NotifyKind.Info)
        => NotifyRaised?.Invoke(new NotifyItem { Title = title, Message = message, Kind = kind, Time = DateTime.Now });

    public static void CheckReminders()
    {
        // 1. 收租提醒（提前天数内未交租）
        var props = AppDb.GetProperties();
        var today = DateTime.Today;
        foreach (var p in props)
        {
            if (p.IsTerminated) continue;
            var due = p.RentDueDay;
            var dueDate = new DateTime(today.Year, today.Month, due > DateTime.DaysInMonth(today.Year, today.Month) ? DateTime.DaysInMonth(today.Year, today.Month) : due);
            var delta = (dueDate - today).Days;
            var lead = int.TryParse(Settings.Get("rentReminderDays"), out var l) ? l : 2;
            if (delta >= 0 && delta <= lead)
                Push("收租提醒", $"{p.RoomNumber} 本期租金应于 {dueDate:MM-dd} 缴交（{p.Rent}元）", NotifyKind.Warning);
        }
        // 2. 租期到期提醒（30天内到期）
        foreach (var h in AppDb.GetHouses().Where(h => !string.IsNullOrEmpty(h.LeaseEnd)))
        {
            if (DateTime.TryParse(h.LeaseEnd, out var end))
            {
                var days = (end.Date - today).Days;
                if (days >= 0 && days <= 30)
                    Push("租期提醒", $"{h.RoomNo} 租期将于 {end:yyyy-MM-dd} 到期（剩 {days} 天）", NotifyKind.Warning);
            }
        }
        // 3. 待办提醒
        foreach (var t in AppDb.GetTodos().Where(t => !t.IsCompleted && t.HasReminder && t.DueDate.HasValue))
        {
            if (t.DueDate.Value.Date <= today)
                Push("待办提醒", t.Title, NotifyKind.Warning);
        }
    }
}

public enum NotifyKind { Info, Warning, Success }

public class NotifyItem
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NotifyKind Kind { get; set; } = NotifyKind.Info;
    public DateTime Time { get; set; }
}
