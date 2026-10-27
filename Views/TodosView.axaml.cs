using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class TodosView : UserControl
{
    private TodosViewModel? Vm => DataContext as TodosViewModel;

    public TodosView() => InitializeComponent();

    private void OnAddTodo(object? sender, RoutedEventArgs e)
        => new TodoEditDialog().ShowDialog(GetWindow(this));

    private void OnToggleComplete(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.Tag is TodoRow row)
            Vm?.ToggleComplete(row);
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is TodoRow row)
            Vm?.MoveUp(row);
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is TodoRow row)
            Vm?.MoveDown(row);
    }

    private void OnEditTodo(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is TodoRow row)
            new TodoEditDialog(row.Todo).ShowDialog(GetWindow(this));
    }

    private void OnDeleteTodo(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is TodoRow row)
        {
            Vm?.DeleteTodo(row);
            NotifyService.Push("待办删除", row.Title, NotifyKind.Info);
        }
    }

    private Window? GetWindow(Avalonia.StyledElement? c)
    {
        while (c != null) { if (c is Window w) return w; c = c.Parent; }
        return null;
    }
}
