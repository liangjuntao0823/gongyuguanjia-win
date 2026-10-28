using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class RentDueCard
{
    public string RoomNumber { get; set; } = "";
    public int Rent { get; set; }
    public int DueDay { get; set; }
    public bool IsOverdue { get; set; }
}

public class PayoutDueCard
{
    public string RoomNumber { get; set; } = "";
    public int DueDay { get; set; }
    public decimal Amount { get; set; }
}
public class ExpiringRow
{
    public string RoomNumber { get; set; } = "";
    public string LeaseEndText { get; set; } = "";
    public string DaysText { get; set; } = "";
}


public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private int houseCount;
    [ObservableProperty]
    private int pendingRentCount;       // 待租房源
    [ObservableProperty]
    private int rentedCount;            // 在租
    [ObservableProperty]
    private decimal monthDealAmount;
    [ObservableProperty]
    private decimal monthRentIncome;
    [ObservableProperty]
    private int openTodoCount;
    [ObservableProperty]
    private decimal monthProfit;
    // v1.0：对齐 iOS 工作台 KPI 卡
    [ObservableProperty]
    private int daysLeftInMonth;      // 本月倒计时天数
    [ObservableProperty]
    private int hoursLeftInMonth;     // 本月倒计时小时
    [ObservableProperty]
    private int manageCount;          // 管理（托管）套数
    [ObservableProperty]
    private int packageCount;         // 包租合同数
    [ObservableProperty]
    private int expiringSoonCount;    // 30天内租期到期
    [ObservableProperty]
    private decimal monthAgentFee;    // 本月中介费

    public List<RentDueCard> RentDueCards { get; private set; } = new();
    public List<RentDueCard> RentDueCards2 { get; private set; } = new();
    public List<PayoutDueCard> PayoutDueCards { get; private set; } = new();
    public List<TodoItem> TodayTodos { get; private set; } = new();
    public List<ExpiringRow> ExpiringSoon { get; private set; } = new();


    // v1.0：对齐 Mac 工作台顶栏（时间/日期/倒计时标语）
    public string CurrentTimeText => DateTime.Now.ToString("HH:mm");
    public string CurrentDateText => DateTime.Now.ToString("M月d日 ddd");
    public string MonthCountdownText => $"{DateTime.Now.Month}月还剩 {DaysLeftInMonth} 天 {HoursLeftInMonth} 小时，加油";
    [ObservableProperty]
    private string userTitle = "";
    [ObservableProperty]
    private string userSubtitle = "";

    public DashboardViewModel() => Refresh();

    public void Refresh()
    {
        UserTitle = Settings.Get("userTitle", "A0-米兰租赁梁涛13288251132");
        UserSubtitle = Settings.Get("userSubtitle", "选择大于一切，专业省心租好房");

        // v1.0：即将到期（30天内租期到期，对齐 Mac 工作台）
        ExpiringSoon = AppDb.GetProperties()
            .Where(p => !p.IsTerminated && DateTime.TryParse(p.LeaseEnd, out _))
            .Select(p => new ExpiringRow
            {
                RoomNumber = p.RoomNumber,
                LeaseEndText = p.LeaseEnd,
                DaysText = (DateTime.Parse(p.LeaseEnd) - DateTime.Now).TotalDays <= 30 ? $"{(int)(DateTime.Parse(p.LeaseEnd) - DateTime.Now).TotalDays}天" : ""
            })
            .Where(r => r.DaysText != "")
            .OrderBy(r => r.LeaseEndText)
            .ToList();
        var houses = AppDb.GetHouses();
        HouseCount = houses.Count;
        PendingRentCount = houses.Count(h => !h.IsRented);
        RentedCount = houses.Count(h => h.IsRented);

        var now = DateTime.Now;
        var deals = AppDb.GetDeals().Where(d => d.Date.Year == now.Year && d.Date.Month == now.Month).ToList();
        MonthDealAmount = deals.Sum(d => d.Amount);

        // 本月收租（当月已收月租 + 收租成交）
        var props = AppDb.GetProperties().Where(p => !p.IsTerminated).ToList();
        MonthRentIncome = props.Sum(p => (decimal)p.Rent);

        var todos = AppDb.GetTodos();
        OpenTodoCount = todos.Count(t => !t.IsCompleted);
        TodayTodos = todos.Where(t => !t.IsCompleted).OrderBy(t => t.SortOrder).Take(6).ToList();

        // v1.0：本月倒计时（对齐 iOS DashboardView timeRemainingInMonth）
        var lastDay = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59);
        var remain = lastDay - now;
        DaysLeftInMonth = remain.Days;
        HoursLeftInMonth = remain.Hours;

        // KPI：管理套数 / 包租 / 30天内到期 / 本月中介费
        ManageCount = props.Count(p => p.Type is "管理" or "托管");
        PackageCount = AppDb.GetPayouts().Count;
        ExpiringSoonCount = props.Count(p =>
            !p.IsTerminated &&
            DateTime.TryParse(p.LeaseEnd, out var le) && le > DateTime.Today && le <= DateTime.Today.AddDays(30));
        MonthAgentFee = deals.Sum(d => d.AgentFee);

        // 未收租提醒（2 列卡片）
        var today = DateTime.Today;
        var dueList = props
            .Where(p => p.RentDueDay > 0)
            .Select(p => new RentDueCard
            {
                RoomNumber = p.RoomNumber,
                Rent = p.Rent,
                DueDay = p.RentDueDay,
                IsOverdue = p.RentDueDay < today.Day,
            })
            .OrderBy(p => p.DueDay)
            .ToList();
        RentDueCards = dueList;
        RentDueCards2 = dueList;

        // 包租付租提醒
        PayoutDueCards = AppDb.GetPayouts()
            .Where(p => p.RentDueDay > 0)
            .Select(p => new PayoutDueCard
            {
                RoomNumber = p.RoomNumber,
                DueDay = p.RentDueDay,
                Amount = (decimal)(p.MonthlyRent ?? p.AnnualRent / 12),
            })
            .OrderBy(p => p.DueDay)
            .ToList();

        // 利润 = 收租收入 - 包租支出 - 杂项支出 + 杂项收入 + 中介费
        var miscIn = AppDb.GetMiscIncomes().Where(m => m.Date.Year == now.Year && m.Date.Month == now.Month).Sum(m => m.Amount);
        var miscOut = AppDb.GetMiscExpenses().Where(m => m.Date.Year == now.Year && m.Date.Month == now.Month).Sum(m => m.Amount);
        var payouts = AppDb.GetPayouts().Sum(p => (decimal)(p.MonthlyRent ?? p.AnnualRent / 12));
        var agentFees = deals.Sum(d => d.AgentFee);
        MonthProfit = MonthRentIncome + miscIn + agentFees - payouts - miscOut;

        OnPropertyChanged(nameof(RentDueCards));
        OnPropertyChanged(nameof(RentDueCards2));
        OnPropertyChanged(nameof(PayoutDueCards));
        OnPropertyChanged(nameof(TodayTodos));
    }

    public void SaveUserCard(string t, string sub)
    {
        UserTitle = t;
        UserSubtitle = sub;
        Settings.Set("userTitle", t);
        Settings.Set("userSubtitle", sub);
    }
}
