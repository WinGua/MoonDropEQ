using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MoonDropEQ.Core;
using MoonDropEQ.Services;

namespace MoonDropEQ.Views;

public partial class DeviceCaptureView : UserControl
{
    private HidCaptureSession _session = new();

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private int _frameCount;
    private bool _started;
    private bool _packaging;

    public DeviceCaptureView()
    {
        InitializeComponent();
    }

    public event EventHandler? CancelRequested;

    public void Reset()
    {
        Stop();
        _session = new HidCaptureSession();
        _frameCount = 0;
        _started = false;
        _packaging = false;

        StatusText.Text = "请先插好耳机，然后点「开始采集」。";
        DetailText.Text = "等待开始。";
        ResetCheck(BuiltInCheck, "内置预设（默认）");
        ResetCheck(CurveCheck, "自定义曲线（8 段 + 提交）");
        ResetCheck(SavedCheck, "保存命令");

        StartButton.Visibility = Visibility.Visible;
        StartButton.IsEnabled = true;
        FinishButton.Visibility = Visibility.Visible;
        FinishButton.IsEnabled = false;
        OpenFolderButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "取消";
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;
        _ = _session.DisposeAsync().AsTask();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        StartButton.IsEnabled = false;
        _cts = new CancellationTokenSource();
        SetStatus("正在识别耳机…");

        try
        {
            try
            {
                var candidates = await Task.Run(HidProbe.Enumerate);
                var best = candidates.FirstOrDefault(candidate => candidate.Score >= 50);

                if (best is null)
                {
                    SetStatus("没有发现耳机");
                    DetailText.Text = "可能是尚未收录的型号。如果官网页面能识别到它，可以继续抓包。";
                }
                else
                {
                    SetStatus($"已识别：{best.DisplayName}");
                    DetailText.Text =
                        $"VID 0x{best.VendorId:X4} / PID 0x{best.ProductId:X4}，输出报文长度 {best.MaxOutputReportLength}。";
                }
            }
            catch
            {
                SetStatus("设备识别失败，将继续抓包");
                DetailText.Text = "识别过程出错，但不影响网页抓包。";
            }

            SetStatus("即将打开浏览器…");
            await _session.StartAsync(_cts.Token);

            SetStatus("请在网页里连接耳机，并应用一次「默认」预设。");
            DetailText.Text = "连接后先应用「默认」，再应用一条「自定义 / 收藏」曲线。两者都需要采集。";
            _pollTask = PollAsync();
        }
        catch (OperationCanceledException)
        {
            // 用户取消
        }
        catch (Exception ex)
        {
            SetStatus("启动失败");
            DetailText.Text = $"{ex.Message}。请安装 Edge 或 Chrome 后重试。";
            StartButton.IsEnabled = true;
            _started = false;
            await _session.StopAsync();
        }
    }

    private async Task PollAsync()
    {
        var token = _cts?.Token ?? CancellationToken.None;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(700, token);
                var snapshot = await _session.SnapshotAsync(token);
                if (!snapshot.Ok)
                {
                    continue;
                }

                _frameCount = snapshot.Frames;
                UpdateChecklist(snapshot);

                DetailText.Text = $"已捕获 {snapshot.Frames} 条报文"
                    + (string.IsNullOrWhiteSpace(snapshot.Last) ? string.Empty : $"；最后一条：{snapshot.Last}");

                FinishButton.IsEnabled = snapshot.Frames > 0;

                if (snapshot.BuiltIn && snapshot.Curve)
                {
                    SetStatus("两条流程都已采集，可以点「完成采集」。");
                }
                else if (snapshot.BuiltIn)
                {
                    SetStatus("已采集内置预设。请再应用一条「自定义 / 收藏」曲线。");
                }
                else
                {
                    SetStatus("请在网页里连接耳机，并应用一次「默认」预设。");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                SetStatus("浏览器连接已断开");
                DetailText.Text = "已捕获的报文会保留，可以点「完成采集」打包。";
                FinishButton.IsEnabled = _frameCount > 0;
                break;
            }
        }
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_packaging)
        {
            return;
        }

        _packaging = true;
        FinishButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        _cts?.Cancel();

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch
            {
                // 轮询已取消
            }
        }

        SetStatus("正在检查读取路径，请不要拔出耳机…");

        try
        {
            string rawLog;
            try
            {
                rawLog = await _session.ReadLogAsync(CancellationToken.None);
            }
            catch
            {
                rawLog = "{\"frames\":[],\"devices\":[]}";
            }

            await _session.StopAsync();

            var log = HidCaptureSession.ParseLog(rawLog);
            var device = log.WebHidDevices.FirstOrDefault();
            var capturedReportId = log.Frames
                .FirstOrDefault(frame => frame.Dir == "in" && frame.ReportId > 0 && frame.ReportId <= 255)
                ?.ReportId
                ?? log.Frames
                    .FirstOrDefault(frame => frame.Dir == "out" && frame.ReportId > 0 && frame.ReportId <= 255)
                    ?.ReportId;
            byte? reportId = capturedReportId is null ? null : (byte)capturedReportId.Value;

            using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var probe = await Task.Run(() => HidProbe.RunProbe(
                device?.VendorId,
                device?.ProductId,
                reportId,
                probeTimeout.Token));

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var pid = device?.ProductId ?? probe.SelectedProductId ?? 0;
            var zipName = $"MoonDropEQ-adapt-{pid:X4}-{timestamp}.zip";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var zipPath = CaptureStorage.CreatePackage(zipName, new (string, string)[]
            {
                ("capture.json", JsonSerializer.Serialize(log, jsonOptions)),
                ("probe.json", JsonSerializer.Serialize(probe, jsonOptions)),
                ("device.txt", BuildDeviceText(device, probe, log)),
                ("summary.md", BuildSummary(device, probe, log, zipName)),
                ("README.txt", BuildReadme())
            });

            SetStatus("采集完成");
            DetailText.Text = $"已生成：{Path.GetFileName(zipPath)}";
            OpenFolderButton.Visibility = Visibility.Visible;
            FinishButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "关闭";
        }
        catch (Exception ex)
        {
            SetStatus("打包失败");
            DetailText.Text = ex.Message;
            FinishButton.IsEnabled = true;
        }
        finally
        {
            _packaging = false;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        CaptureStorage.OpenFolder();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_packaging)
        {
            return;
        }

        Stop();
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateChecklist(CaptureSnapshot snapshot)
    {
        SetCheck(BuiltInCheck, snapshot.BuiltIn, "内置预设（默认）");
        SetCheck(CurveCheck, snapshot.Curve, "自定义曲线（8 段 + 提交）");
        SetCheck(SavedCheck, snapshot.Saved, "保存命令");
    }

    private void SetCheck(TextBlock target, bool done, string label)
    {
        target.Text = (done ? "● " : "○ ") + label;
        target.Foreground = (Brush)FindResource(done ? "AppSuccessBrush" : "AppTextTertiaryBrush");
    }

    private void ResetCheck(TextBlock target, string label)
    {
        target.Text = "○ " + label;
        target.Foreground = (Brush)FindResource("AppTextTertiaryBrush");
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private static string BuildDeviceText(WebHidDeviceInfo? device, ProbeReport probe, CaptureLog log)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MoonDropEQ 设备适配采集");
        builder.AppendLine($"采集时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"系统：{Environment.OSVersion.VersionString}");
        builder.AppendLine();
        builder.AppendLine("官网识别到的设备：");

        if (device is null)
        {
            builder.AppendLine("- 未识别");
        }
        else
        {
            builder.AppendLine($"- 名称：{(string.IsNullOrWhiteSpace(device.ProductName) ? "未知" : device.ProductName)}");
            builder.AppendLine($"- VID：0x{device.VendorId:X4}");
            builder.AppendLine($"- PID：0x{device.ProductId:X4}");
        }

        builder.AppendLine();
        builder.AppendLine(
            $"报文：{log.Frames.Count} 条（发送 {log.Frames.Count(frame => frame.Dir == "out")} / "
            + $"接收 {log.Frames.Count(frame => frame.Dir == "in")}）");
        builder.AppendLine($"探测：{probe.Note}");
        return builder.ToString();
    }

    private static string BuildSummary(
        WebHidDeviceInfo? device,
        ProbeReport probe,
        CaptureLog log,
        string zipName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MoonDropEQ 设备适配采集摘要");
        builder.AppendLine();
        builder.AppendLine($"- 采集文件：`{zipName}`");
        builder.AppendLine($"- 采集时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine(
            $"- 官网设备：{(device is null ? "未识别" : $"{device.ProductName}（VID 0x{device.VendorId:X4} / PID 0x{device.ProductId:X4}）")}");
        builder.AppendLine();
        builder.AppendLine("## 已采集流程");
        builder.AppendLine(
            $"- 内置预设（默认）：{(log.Frames.Any(frame => StartsWith(frame, 0x01, 0x0F, 0x01)) ? "有" : "无")}");
        builder.AppendLine(
            $"- 自定义曲线：{(log.Frames.Any(frame => StartsWith(frame, 0x01, 0x0A)) ? "有" : "无")}");
        builder.AppendLine(
            $"- 保存命令：{(log.Frames.Any(frame => StartsWith(frame, 0x01, 0x01, 0x00)) ? "有" : "无")}");
        builder.AppendLine();
        builder.AppendLine("## 读取探测");
        builder.AppendLine("- 只读：是（未发送任何写入命令）");
        builder.AppendLine($"- 结果：{probe.Note}");

        if (probe.SelectedVendorId is not null)
        {
            builder.AppendLine(
                $"- 设备：{probe.SelectedProductName}（VID 0x{probe.SelectedVendorId:X4} / "
                + $"PID 0x{probe.SelectedProductId:X4}）");
            builder.AppendLine($"- 当前槽位：{probe.ActiveIndex}");
            builder.AppendLine($"- PEQ 段数：{probe.Bands.Count}");
            if (probe.PreGainDb is not null)
            {
                builder.AppendLine($"- 前级增益：{probe.PreGainDb:0.0} dB");
            }
            if (probe.GlobalGainDb is not null)
            {
                builder.AppendLine($"- 全局增益：{probe.GlobalGainDb:0.0} dB");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 下一步");
        builder.AppendLine("打开 MoonDropEQ → 设备适配 → 采集提交，选择提交 Issue 或按 PR 指南提交。");
        return builder.ToString();
    }

    private static string BuildReadme()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "MoonDropEQ 设备适配采集包",
            string.Empty,
            "包含文件：",
            "- summary.md：采集摘要，建议提交时一并粘贴",
            "- device.txt：设备和系统信息（不含序列号）",
            "- capture.json：官网页面与耳机之间的 HID 报文",
            "- probe.json：只读探测结果",
            string.Empty,
            "提交方式：",
            "1. 打开 MoonDropEQ → 设备适配 → 采集提交 → 提交 Issue，把整个 zip 拖进 Issue。",
            "2. 或者按 PR 指南，在 devices.json 中新增设备描述后提交 Pull Request。",
            string.Empty,
            "隐私：采集包不包含账号、页面内容或设备序列号，只记录 HID 报文。"
        });
    }

    private static bool StartsWith(CaptureFrame frame, params byte[] expected)
    {
        var parts = frame.Data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (!byte.TryParse(
                    parts[index],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value != expected[index])
            {
                return false;
            }
        }

        return true;
    }
}
