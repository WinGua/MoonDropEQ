using System.Windows;
using Wpf.Ui.Controls;

namespace MoonDropEQ;

public partial class RenameDialog : FluentWindow
{
    private RenameDialog(string title, string hint, string initial)
    {
        InitializeComponent();
        Services.UiScaleHelper.Apply(this, 360);
        Title = title;
        HintText.Text = hint;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string? Result { get; private set; }

    public static string? Show(Window owner, string title, string hint, string initial)
    {
        var dialog = new RenameDialog(title, hint, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var value = NameBox.Text.Trim();
        if (value.Length == 0)
        {
            NameBox.Focus();
            return;
        }

        Result = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
