using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MoonDropEQ.Core;
using MoonDropEQ.Services;

namespace MoonDropEQ.Views;

public partial class DeviceAdapterView : UserControl
{
    public DeviceAdapterView()
    {
        InitializeComponent();
    }

    public void RefreshStatus()
    {
        var packages = CaptureStorage.ListPackages();
        PackageCountText.Text = packages.Count == 0 ? "暂无" : $"{packages.Count} 个";
        LastCaptureText.Text = packages.Count == 0
            ? "暂无"
            : packages[0].LastWriteTime.ToString("yyyy-MM-dd HH:mm");
        HintText.Text = string.Empty;
        _ = RefreshDeviceAsync();
    }

    private async Task RefreshDeviceAsync()
    {
        DeviceText.Text = "正在检测…";
        try
        {
            var candidates = await Task.Run(HidProbe.Enumerate);
            var best = candidates.FirstOrDefault(candidate => candidate.Score >= 50);
            DeviceText.Text = best is null
                ? "未检测到耳机"
                : $"{best.DisplayName}（VID 0x{best.VendorId:X4} / PID 0x{best.ProductId:X4}）";
        }
        catch
        {
            DeviceText.Text = "检测失败";
        }
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DeviceCaptureDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
        RefreshStatus();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureStorage.ListPackages().Count == 0)
        {
            HintText.Text = "还没有采集数据，先点「设备采集」。";
            return;
        }

        CaptureStorage.OpenFolder();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SubmitGuideDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
