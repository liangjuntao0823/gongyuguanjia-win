using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace ApartmentManagerWin.Views;

/// <summary>通用确认对话框</summary>
public class ConfirmDialog : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    private ConfirmDialog(string title, string message)
    {
        Title = title;
        Width = 440;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
        var msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(16, 7) };
        var ok = new Button { Content = "确定", Padding = new Thickness(16, 7), Background = new SolidColorBrush(Color.Parse("#2F6BFF")), Foreground = Brushes.White };
        cancel.Click += (_, _) => { _tcs.TrySetResult(false); Close(); };
        ok.Click += (_, _) => { _tcs.TrySetResult(true); Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        panel.Children.Add(msg);
        panel.Children.Add(btns);
        Content = panel;
        Closed += (_, _) => _tcs.TrySetResult(false);
    }

    public static Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog(title, message);
        dlg.ShowDialog(owner);
        return dlg._tcs.Task;
    }
}
