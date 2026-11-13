using System.Diagnostics;
using System.Windows;
using MoonDropEQ.Services;
using Wpf.Ui.Controls;

namespace MoonDropEQ.Views;

public partial class SubmitGuideDialog : FluentWindow
{
    private const string IssueUrl =
        "https://github.com/WinGua/MoonDropEQ/issues/new?template=device-support.yml";

    private const string GuideUrl =
        "https://github.com/WinGua/MoonDropEQ/blob/main/docs/adding-device.md";

    public SubmitGuideDialog()
    {
        InitializeComponent();
        ThemeService.ApplyBackdrop(this);

        var latest = CaptureStorage.LatestPackage();
        PackageText.Text = latest is null
            ? "还没有采集包。请先在「设备采集」里完成一次采集。"
            : $"采集包：{latest.Name}";
    }

    private void Issue_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(IssueUrl);
    }

    private void Pr_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(GuideUrl);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
