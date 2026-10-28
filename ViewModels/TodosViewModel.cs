using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.ViewModels;

public class TodoRow
{
    public TodoItem Todo { get; set; } = new();
    public long Id => Todo.Id;
    public string Title => Todo.Title;
    public string Note => Todo.Note;
    public bool IsCompleted => Todo.IsCompleted;
    public string DueText => Todo.DueDate.HasValue ? Todo.DueDate.Value.ToString("MM-dd") : "";
    public string CompletedText => Todo.CompletedDate.HasValue ? "完成于 " + Todo.CompletedDate.Value.ToString("MM-dd HH:mm") : "";
    public string ReminderText => Todo.HasReminder ? "⏰" : "";
    public bool HasDue => Todo.DueDate.HasValue;
}

public partial class TodosViewModel : ViewModelBase
{
    public ObservableCollection<TodoRow> Rows { get; } = new();
    public ObservableCollection<string> FilterOptions { get; } = new() { "全部", "未办", "已办" };

    [ObservableProperty]
    private string searchText = "";
    [ObservableProperty]
    private string selectedFilter = "未办";
    [ObservableProperty]
    private string statText = "";

    public TodosViewModel()
    {
        AppDb.DataChanged += Refresh;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedFilterChanged(string value) => Refresh();

    public void Refresh()
    {
        Rows.Clear();
        var list = AppDb.GetTodos();
        var filtered = SelectedFilter switch
        {
            "未办" => list.Where(t => !t.IsCompleted),
            "已办" => list.Where(t => t.IsCompleted),
            _ => list,
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(t => t.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var t in filtered.OrderBy(t => t.IsCompleted).ThenBy(t => t.SortOrder).ThenByDescending(t => t.CreatedAt))
            Rows.Add(new TodoRow { Todo = t });
        var open = list.Count(t => !t.IsCompleted);
        StatText = $"未办 {open} 项 ｜ 显示 {Rows.Count} 项";
        OnPropertyChanged(nameof(StatText));
    }

    public void ToggleComplete(TodoRow row)
    {
        row.Todo.IsCompleted = !row.Todo.IsCompleted;
        AppDb.SaveTodo(row.Todo);
        AppDb.AddLog("待办", row.Todo.IsCompleted ? "完成" : "重开", "成功", row.Todo.Title);
        AppDb.NotifyDataChanged();
    }

    public void DeleteTodo(TodoRow row)
    {
        AppDb.SoftDeleteTodo(row.Todo.Id);
        AppDb.AddLog("待办", "删除", "成功", row.Todo.Title);
        AppDb.NotifyDataChanged();
    }

    public void MoveUp(TodoRow row)
    {
        var all = AppDb.GetTodos().Where(t => !t.IsDeleted && !t.IsCompleted).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
        var idx = all.FindIndex(t => t.Id == row.Todo.Id);
        if (idx <= 0) return;
        (all[idx - 1].SortOrder, all[idx].SortOrder) = (all[idx].SortOrder, all[idx - 1].SortOrder);
        AppDb.SaveTodo(all[idx - 1]);
        AppDb.SaveTodo(all[idx]);
        AppDb.NotifyDataChanged();
    }

    public void MoveDown(TodoRow row)
    {
        var all = AppDb.GetTodos().Where(t => !t.IsDeleted && !t.IsCompleted).OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
        var idx = all.FindIndex(t => t.Id == row.Todo.Id);
        if (idx < 0 || idx >= all.Count - 1) return;
        (all[idx + 1].SortOrder, all[idx].SortOrder) = (all[idx].SortOrder, all[idx + 1].SortOrder);
        AppDb.SaveTodo(all[idx + 1]);
        AppDb.SaveTodo(all[idx]);
        AppDb.NotifyDataChanged();
    }
}
