using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MoonDropEQ;

public partial class HotkeyDialog : FluentWindow
{
    private string _gesture;

    private HotkeyDialog(string presetName, string current)
    {
        InitializeComponent();

        _gesture = current;
        PresetText.Text = $"为「{presetName}」设置热键";
        GestureText.Text = string.IsNullOrWhiteSpace(current) ? "未绑定" : current;
        Services.ThemeService.ApplyBackdrop(this);
        Services.UiScaleHelper.Apply(this, 380);
    }

    public static string? Show(Window owner, string presetName, string current)
    {
        var dialog = new HotkeyDialog(presetName, current) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._gesture : null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        string? name = key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + ((int)key - (int)Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + ((int)key - (int)Key.NumPad0))).ToString(),
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            _ => null
        };

        e.Handled = true;

        if (name is null)
        {
            StatusText.Text = "仅支持字母、数字和 F1–F24";
            return;
        }

        if (parts.Count == 0 && !name.StartsWith("F", StringComparison.Ordinal))
        {
            StatusText.Text = "请至少加一个修饰键（Ctrl / Alt / Shift / Win）";
            return;
        }

        parts.Add(name);
        _gesture = string.Join("+", parts);
        GestureText.Text = _gesture;
        StatusText.Text = "按「确定」应用";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _gesture = string.Empty;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
