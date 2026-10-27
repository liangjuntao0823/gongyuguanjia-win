using ApartmentManagerWin.Services;
using Avalonia.Controls;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private MainViewModel? Main => Nav.Current;

    private void OnAddHouse(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("houses");

    private void OnAddDeal(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("deals");

    private void OnGoRent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("rent");

    private void OnGoPayout(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("payout");

    private void OnGoTodos(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Main?.GoTo("todos");
}
