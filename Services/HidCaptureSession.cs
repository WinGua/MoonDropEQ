using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using MoonDropEQ.Core;

namespace MoonDropEQ.Services;

public sealed class WebHidDeviceInfo
{
    public int VendorId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
}

public sealed class CaptureFrame
{
    public long T { get; set; }

    public string Dir { get; set; } = "out";

    public int ReportId { get; set; }

    public string Data { get; set; } = string.Empty;
}

public sealed class CaptureLog
{
    public int Schema { get; set; } = 1;

    public string StartedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    public string Url { get; set; } = HidCaptureSession.HubUrl;

    public List<WebHidDeviceInfo> WebHidDevices { get; set; } = new();

    public List<CaptureFrame> Frames { get; set; } = new();
}

public sealed class CaptureSnapshot
{
    public bool Ok { get; set; }

    public int Frames { get; set; }

    public bool BuiltIn { get; set; }

    public bool Curve { get; set; }

    public bool Saved { get; set; }

    public List<WebHidDeviceInfo> Devices { get; set; } = new();

    public string Last { get; set; } = string.Empty;
}

/// <summary>
/// 采集判定的期望报文。已知机型按设备描述生成，未知机型回退到结构特征。
/// </summary>
public sealed class CaptureRules
{
    public bool Known { get; set; }

    public int BandPayloadSize { get; set; }

    public int[]? BuiltIn { get; set; }

    public int[]? Band { get; set; }

    public int[]? Save { get; set; }

    public static CaptureRules FromProfile(DeviceProfile? profile)
    {
        if (profile is null)
        {
            return new CaptureRules();
        }

        return new CaptureRules
        {
            Known = true,
            BandPayloadSize = profile.BandPayloadSize,
            BuiltIn = profile.Capabilities.BuiltInSlots
                ? new[] { (int)profile.WriteOpValue, (int)profile.Commands.EqIndexValue, 1 }
                : null,
            Band = profile.Capabilities.Curve
                ? new[] { (int)profile.WriteOpValue, (int)profile.Commands.EqBandValue }
                : null,
            Save = new[] { (int)profile.WriteOpValue, (int)profile.Commands.SaveValue, 0 }
        };
    }

    /// <summary>
    /// 离线判定采集包内容。未知机型按报文长度推断：单字节命令、PEQ 段、空载荷命令。
    /// </summary>
    public static CaptureChecks Evaluate(CaptureLog log, CaptureRules rules)
    {
        var outgoing = log.Frames.Where(frame => frame.Dir == "out").ToList();
        if (rules.Known)
        {
            return new CaptureChecks(
                outgoing.Any(frame => Matches(frame, rules.BuiltIn)),
                outgoing.Any(frame => Matches(frame, rules.Band)),
                outgoing.Any(frame => Matches(frame, rules.Save)));
        }

        return new CaptureChecks(
            outgoing.Any(frame => ByteCount(frame) == 4),
            outgoing.Any(frame => ByteCount(frame) >= 8),
            outgoing.Any(frame => ByteCount(frame) <= 3));
    }

    private static bool Matches(CaptureFrame frame, int[]? prefix)
    {
        if (prefix is null || prefix.Length == 0)
        {
            return false;
        }

        var parts = frame.Data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var offset = 0; offset <= 1; offset++)
        {
            if (parts.Length < offset + prefix.Length)
            {
                continue;
            }

            var matched = true;
            for (var index = 0; index < prefix.Length; index++)
            {
                if (!byte.TryParse(
                        parts[offset + index],
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var value)
                    || value != prefix[index])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static int ByteCount(CaptureFrame frame)
    {
        return frame.Data.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

public readonly record struct CaptureChecks(bool BuiltIn, bool Curve, bool Saved);

/// <summary>
/// 用调试模式启动 Edge/Chrome，通过 CDP 注入钩子，记录官网页面与耳机之间的 HID 报文。
/// </summary>
public sealed class HidCaptureSession : IAsyncDisposable
{
    public const string HubUrl = "https://hub.moondroplab.tech/";

    private const string HookScript = """
(() => {
  if (window.__mdEq) return 'installed';
  const state = { frames: [], devices: [] };
  window.__mdEq = state;

  const hex = value => {
    try {
      let bytes;
      if (value instanceof DataView) {
        bytes = new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
      } else if (ArrayBuffer.isView(value)) {
        bytes = new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
      } else if (value instanceof ArrayBuffer) {
        bytes = new Uint8Array(value);
      } else {
        bytes = new Uint8Array(value);
      }
      return Array.from(bytes).map(byte => byte.toString(16).padStart(2, '0')).join(' ');
    } catch (error) {
      return '';
    }
  };

  const push = (dir, reportId, data) => {
    const text = hex(data);
    if (!text) return;
    state.frames.push({ t: Math.round(performance.now()), dir, reportId: reportId || 0, data: text });
    if (state.frames.length > 20000) {
      state.frames.splice(0, state.frames.length - 20000);
    }
  };

  const bound = new WeakSet();
  const remember = device => {
    if (!device) return;

    try {
      const meta = {
        vendorId: device.vendorId,
        productId: device.productId,
        productName: device.productName || ''
      };
      const existing = state.devices.find(item =>
        item.vendorId === meta.vendorId &&
        item.productId === meta.productId);
      if (existing) {
        if (!existing.productName && meta.productName) {
          existing.productName = meta.productName;
        }
      } else {
        state.devices.push(meta);
      }
    } catch (error) {
    }

    if (bound.has(device)) return;
    bound.add(device);

    try {
      device.addEventListener('inputreport', event => push('in', event.reportId, event.data));
    } catch (error) {
    }
  };

  if (navigator.hid) {
    const hidPrototype = Object.getPrototypeOf(navigator.hid);
    const requestDevice = hidPrototype.requestDevice;
    if (requestDevice) {
      hidPrototype.requestDevice = async function (options) {
        const devices = await requestDevice.call(this, options);
        (devices || []).forEach(remember);
        return devices;
      };
    }

    const getDevices = hidPrototype.getDevices;
    if (getDevices) {
      hidPrototype.getDevices = async function () {
        const devices = await getDevices.call(this);
        (devices || []).forEach(remember);
        return devices;
      };
    }
  }

  if (window.HIDDevice) {
    const sendReport = HIDDevice.prototype.sendReport;
    if (sendReport) {
      HIDDevice.prototype.sendReport = function (reportId, data) {
        push('out', reportId, data);
        return sendReport.apply(this, arguments);
      };
    }

    const sendFeatureReport = HIDDevice.prototype.sendFeatureReport;
    if (sendFeatureReport) {
      HIDDevice.prototype.sendFeatureReport = function (reportId, data) {
        push('out', reportId, data);
        return sendFeatureReport.apply(this, arguments);
      };
    }

    const open = HIDDevice.prototype.open;
    if (open) {
      HIDDevice.prototype.open = async function () {
        const result = await open.apply(this, arguments);
        remember(this);
        return result;
      };
    }
  }

  return 'ok';
})()
""";

    private const string SnapshotExpression = """
(() => {
  const state = window.__mdEq;
  if (!state) return JSON.stringify({ ok: false });

  const rules = __RULES__;
  const parse = value => value.split(' ').filter(Boolean).map(item => parseInt(item, 16));
  const startsWith = (frame, expected) => {
    if (!expected || !expected.length) return false;
    const bytes = parse(frame.data);
    for (const offset of [0, 1]) {
      if (bytes.length < offset + expected.length) continue;
      let matched = true;
      for (let index = 0; index < expected.length; index++) {
        if (bytes[offset + index] !== expected[index]) {
          matched = false;
          break;
        }
      }
      if (matched) return true;
    }
    return false;
  };

  const outgoing = state.frames.filter(frame => frame.dir === 'out');
  const known = rules.known === true;
  const builtIn = known
    ? outgoing.some(frame => startsWith(frame, rules.builtIn))
    : outgoing.some(frame => parse(frame.data).length === 4);
  const curve = known
    ? outgoing.some(frame => startsWith(frame, rules.band))
    : outgoing.some(frame => parse(frame.data).length >= 8);
  const saved = known
    ? outgoing.some(frame => startsWith(frame, rules.save))
    : outgoing.some(frame => parse(frame.data).length <= 3);

  return JSON.stringify({
    ok: true,
    frames: state.frames.length,
    builtIn,
    curve,
    saved,
    devices: state.devices.map(device => ({
      vendorId: device.vendorId,
      productId: device.productId,
      productName: device.productName
    })),
    last: outgoing.length ? outgoing[outgoing.length - 1].data.slice(0, 23) : ''
  });
})()
""";

    private const string LogExpression =
        "JSON.stringify(window.__mdEq ? { frames: window.__mdEq.frames, devices: window.__mdEq.devices } : { frames: [], devices: [] })";

    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private Process? _process;
    private ClientWebSocket? _ws;
    private string _tempProfile = string.Empty;
    private int _nextId = 100;

    public int Port { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var browserPath = FindBrowser() ?? throw new InvalidOperationException("未找到 Edge 或 Chrome");
        Port = GetFreePort();
        _tempProfile = Path.Combine(
            Path.GetTempPath(),
            "MoonDropEQ-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempProfile);

        var startInfo = new ProcessStartInfo(browserPath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--remote-debugging-port={Port}");
        startInfo.ArgumentList.Add("--remote-allow-origins=*");
        startInfo.ArgumentList.Add($"--user-data-dir={_tempProfile}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        startInfo.ArgumentList.Add("--disable-features=msEdgeFirstRunExperience");
        startInfo.ArgumentList.Add("about:blank");

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动浏览器");

        await WaitForDevToolsAsync(cancellationToken);
        var pageUrl = await GetPageTargetAsync(cancellationToken);

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
        await _ws.ConnectAsync(new Uri(pageUrl), cancellationToken);

        await SendCommandAsync("Page.enable", "{}", cancellationToken);
        await SendCommandAsync("Runtime.enable", "{}", cancellationToken);
        await SendCommandAsync(
            "Page.addScriptToEvaluateOnNewDocument",
            JsonSerializer.Serialize(new { source = HookScript }),
            cancellationToken);
        await SendCommandAsync(
            "Page.navigate",
            JsonSerializer.Serialize(new { url = HubUrl }),
            cancellationToken);
    }

    public async Task<CaptureSnapshot> SnapshotAsync(CaptureRules rules, CancellationToken cancellationToken)
    {
        var expression = SnapshotExpression.Replace(
            "__RULES__",
            JsonSerializer.Serialize(
                rules,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var value = await EvaluateStringAsync(expression, cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return new CaptureSnapshot();
        }

        return JsonSerializer.Deserialize<CaptureSnapshot>(
            value,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new CaptureSnapshot();
    }

    public async Task<string> ReadLogAsync(CancellationToken cancellationToken)
    {
        var value = await EvaluateStringAsync(LogExpression, cancellationToken);
        return string.IsNullOrWhiteSpace(value)
            ? "{\"frames\":[],\"devices\":[]}"
            : value;
    }

    public static CaptureLog ParseLog(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<RawCapture>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new RawCapture();

            return new CaptureLog
            {
                Frames = raw.Frames,
                WebHidDevices = raw.Devices
                    .Select(device => new WebHidDeviceInfo
                    {
                        VendorId = device.VendorId,
                        ProductId = device.ProductId,
                        ProductName = device.ProductName
                    })
                    .ToList()
            };
        }
        catch
        {
            return new CaptureLog();
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                await SendCommandNoWaitAsync("Browser.close", "{}");
            }
        }
        catch
        {
            // 浏览器可能已关闭
        }

        try
        {
            _ws?.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }
        _ws = null;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 忽略关闭异常
        }
        _process?.Dispose();
        _process = null;

        await Task.Delay(150);

        try
        {
            if (Directory.Exists(_tempProfile))
            {
                Directory.Delete(_tempProfile, true);
            }
        }
        catch
        {
            // 临时目录可能仍被浏览器占用
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _http.Dispose();
    }

    private async Task WaitForDevToolsAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await _http.GetStringAsync($"http://127.0.0.1:{Port}/json/version", cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 浏览器尚未就绪
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException("浏览器启动超时");
    }

    private async Task<string> GetPageTargetAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await _http.GetStringAsync($"http://127.0.0.1:{Port}/json/list", cancellationToken);
                using var document = JsonDocument.Parse(text);
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("type", out var type)
                        || type.GetString() != "page"
                        || !element.TryGetProperty("webSocketDebuggerUrl", out var url))
                    {
                        continue;
                    }

                    var value = url.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // 页面尚未就绪
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException("找不到浏览器页面");
    }

    private async Task<JsonDocument> SendCommandAsync(
        string method,
        string paramsJson,
        CancellationToken cancellationToken)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("浏览器连接已断开");
        }

        var id = _nextId++;
        var payload = $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
        await _ws.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

        var response = await ReceiveUntilIdAsync(id, cancellationToken);
        return JsonDocument.Parse(response);
    }

    private async Task SendCommandNoWaitAsync(string method, string paramsJson)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
        {
            return;
        }

        var id = _nextId++;
        var payload = $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _ws.SendAsync(
            new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)),
            WebSocketMessageType.Text,
            true,
            timeout.Token);
    }

    private async Task<string> ReceiveUntilIdAsync(int id, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        while (true)
        {
            stream.SetLength(0);
            stream.Position = 0;
            WebSocketReceiveResult result;

            do
            {
                result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("浏览器连接已关闭");
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var text = Encoding.UTF8.GetString(stream.ToArray());
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("id", out var idElement)
                && idElement.TryGetInt32(out var responseId)
                && responseId == id)
            {
                return text;
            }
        }
    }

    private async Task<string?> EvaluateStringAsync(string expression, CancellationToken cancellationToken)
    {
        using var document = await SendCommandAsync(
            "Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, returnByValue = true }),
            cancellationToken);

        if (!document.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("result", out var inner)
            || !inner.TryGetProperty("value", out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static string? FindBrowser()
    {
        foreach (var name in new[] { "msedge.exe", "chrome.exe" })
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = hive.OpenSubKey(
                        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}");
                    if (key?.GetValue(null) is string path && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // 注册表不可读时继续查找
                }
            }
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        var relatives = new[]
        {
            @"Microsoft\Edge\Application\msedge.exe",
            @"Google\Chrome\Application\chrome.exe"
        };

        foreach (var root in roots)
        {
            foreach (var relative in relatives)
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

}

public sealed class RawCapture
{
    public List<CaptureFrame> Frames { get; set; } = new();

    public List<RawDevice> Devices { get; set; } = new();
}

public sealed class RawDevice
{
    public int VendorId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
}
