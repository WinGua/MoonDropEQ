using System.Windows;
using Wpf.Ui.Controls;

namespace MoonDropEQ;

public partial class ConfirmDialog : FluentWindow
{
    private ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();

        Title = title;
        DialogTitleBar.Title = title;
        MessageText.Text = message;
        OkButton.Content = confirmText;
        Services.ThemeService.ApplyBackdrop(this);
    }

    public static bool Show(Window owner, string title, string message, string confirmText = "确定")
    {
        var dialog = new ConfirmDialog(title, message, confirmText) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
