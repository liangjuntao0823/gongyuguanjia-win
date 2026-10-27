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

    public List<RentDueCard> RentDueCards { get; private set; } = new();
    public List<RentDueCard> RentDueCards2 { get; private set; } = new();
    public List<PayoutDueCard> PayoutDueCards { get; private set; } = new();
    public List<TodoItem> TodayTodos { get; private set; } = new();

    public DashboardViewModel() => Refresh();

    public void Refresh()
    {
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
}
