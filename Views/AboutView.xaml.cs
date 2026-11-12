using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MoonDropEQ.Views;

public partial class AboutView : UserControl
{
    private const string LatestReleaseApi = "https://api.github.com/repos/WinGua/MoonDropEQ/releases/latest";
    private const string ReleasesPage = "https://github.com/WinGua/MoonDropEQ/releases";

    private string _downloadUrl = ReleasesPage;

    public AboutView()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null
            ? "v0.1.0"
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = "正在检查更新…";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MoonDropEQ");

            using var response = await http.GetAsync(LatestReleaseApi);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _downloadUrl = ReleasesPage;
                UpdateStatus.Text = "还没有发布版本，可前往 Releases 页面查看。";
                DownloadButton.Visibility = Visibility.Visible;
                return;
            }

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tag = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            _downloadUrl = document.RootElement.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? ReleasesPage
                : ReleasesPage;

            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var latest = ParseVersion(tag);

            if (latest is not null && latest.CompareTo(current) > 0)
            {
                UpdateStatus.Text = $"发现新版本 {tag}（当前 v{current.Major}.{current.Minor}.{current.Build}）";
                DownloadButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatus.Text = string.IsNullOrEmpty(tag)
                    ? "当前已是最新版本"
                    : $"已是最新版本（{tag}）";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "检查失败：" + ex.Message;
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_downloadUrl) { UseShellExecute = true });
    }

    private static Version? ParseVersion(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }
}
