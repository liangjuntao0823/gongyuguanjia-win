using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Views;

public partial class TodoEditDialog : Window
{
    private readonly TodoItem? _editing;

    public TodoEditDialog(TodoItem? editing = null)
    {
        InitializeComponent();
        _editing = editing;
        TitleText.Text = editing == null ? "添加待办" : "编辑待办";
        DuePicker.SelectedDate = DateTime.Today;
        if (editing != null)
        {
            TitleBox.Text = editing.Title;
            NoteBox.Text = editing.Note;
            ReminderToggle.IsChecked = editing.HasReminder;
            if (editing.DueDate.HasValue) DuePicker.SelectedDate = new DateTimeOffset(editing.DueDate.Value);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { ErrorText.Text = "请填写标题"; return; }
        var t = _editing ?? new TodoItem();
        t.Title = TitleBox.Text.Trim();
        t.Note = NoteBox.Text?.Trim() ?? "";
        t.HasReminder = ReminderToggle.IsChecked == true;
        t.DueDate = DuePicker.SelectedDate?.Date;
        var isNew = t.Id == 0;
        AppDb.SaveTodo(t);
        AppDb.AddLog("待办", isNew ? "新增" : "编辑", "成功", t.Title);
        AppDb.NotifyDataChanged();
        Close();
    }
}
