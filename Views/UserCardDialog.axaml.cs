using Avalonia.Controls;
using Avalonia.Interactivity;
using ApartmentManagerWin.ViewModels;

namespace ApartmentManagerWin.Views;

public partial class UserCardDialog : Window
{
    private readonly DashboardViewModel? _vm;

    public UserCardDialog(DashboardViewModel? vm)
    {
        InitializeComponent();
        _vm = vm;
        if (_vm != null)
        {
            TitleBox.Text = _vm.UserTitle;
            SubtitleBox.Text = _vm.UserSubtitle;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _vm?.SaveUserCard(TitleBox.Text ?? "", SubtitleBox.Text ?? "");
        Close();
    }
}
