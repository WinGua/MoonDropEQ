using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using HidSharp;

namespace MoonDropEQ.Core;

public sealed class HidCandidate
{
    public int VendorId { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public int MaxOutputReportLength { get; set; }

    public int MaxInputReportLength { get; set; }

    public int Score { get; set; }

    public bool Responded { get; set; }

    public int? ActiveIndex { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(ProductName)
        ? $"未知设备（VID 0x{VendorId:X4} / PID 0x{ProductId:X4}）"
        : ProductName;

    [JsonIgnore]
    public HidDevice? Device { get; set; }
}

public sealed class BandProbe
{
    public int Index { get; set; }

    public bool Ok { get; set; }

    public int Length { get; set; }

    public string Hex { get; set; } = string.Empty;
}

public sealed class ProbeReport
{
    public int Schema { get; set; } = 1;

    public bool ReadOnly { get; set; } = true;

    public string CollectedAt { get; set; } = DateTimeOffset.Now.ToString("O");

    public List<HidCandidate> Candidates { get; set; } = new();

    public int? SelectedVendorId { get; set; }

    public int? SelectedProductId { get; set; }

    public string? SelectedProductName { get; set; }

    public int? ActiveIndex { get; set; }

    public List<BandProbe> Bands { get; set; } = new();

    public double? PreGainDb { get; set; }

    public double? GlobalGainDb { get; set; }

    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// 新设备适配用的只读探测。只发送读命令（op = 0x80），不会写入耳机。
/// </summary>
public static class HidProbe
{
    private const int KnownVendorId = 0x35D8;

    public static List<HidCandidate> Enumerate()
    {
        var result = new List<HidCandidate>();

        foreach (var device in DeviceList.Local.GetHidDevices())
        {
            try
            {
                var outLength = SafeLength(device.GetMaxOutputReportLength);
                var inLength = SafeLength(device.GetMaxInputReportLength);
                var product = SafeString(device.GetProductName);
                var manufacturer = SafeString(device.GetManufacturer);

                var knownVendor = device.VendorID == KnownVendorId;
                var productMatch = ContainsMoondrop(product) || ContainsMoondrop(manufacturer);
                if (!knownVendor && !productMatch && outLength < 37)
                {
                    continue;
                }

                var score = 0;
                if (knownVendor)
                {
                    score += 50;
                }
                if (productMatch)
                {
                    score += 30;
                }
                if (outLength >= 64)
                {
                    score += 20;
                }

                result.Add(new HidCandidate
                {
                    VendorId = device.VendorID,
                    ProductId = device.ProductID,
                    ProductName = product,
                    Manufacturer = manufacturer,
                    DevicePath = device.DevicePath,
                    MaxOutputReportLength = outLength,
                    MaxInputReportLength = inLength,
                    Score = score,
                    Device = device
                });
            }
            catch
            {
                // 单个设备读取失败不影响整体枚举
            }
        }

        return result
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.VendorId)
            .ThenBy(candidate => candidate.ProductId)
            .ToList();
    }

    public static ProbeReport RunProbe(
        int? vendorId,
        int? productId,
        byte? reportId,
        CancellationToken cancellationToken = default)
    {
        var report = new ProbeReport();
        List<HidCandidate> candidates;
        try
        {
            candidates = Enumerate();
        }
        catch (Exception ex)
        {
            report.Note = $"枚举 HID 设备失败：{ex.Message}";
            return report;
        }

        report.Candidates.AddRange(candidates);

        if (candidates.Count == 0)
        {
            report.Note = "没有发现可用的 HID 接口。";
            return report;
        }

        var targets = SelectTargets(candidates, vendorId, productId);
        if (targets.Count == 0)
        {
            report.Note = "没有找到可以安全探测的设备，请以抓包数据为准。";
            return report;
        }

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadTarget(target, reportId, report, cancellationToken))
            {
                return report;
            }
        }

        report.Note = "设备没有回应读取命令，抓包数据仍然有效。";
        return report;
    }

    private static List<HidCandidate> SelectTargets(
        List<HidCandidate> candidates,
        int? vendorId,
        int? productId)
    {
        if (vendorId.HasValue)
        {
            var exact = candidates
                .Where(candidate => candidate.VendorId == vendorId
                    && (!productId.HasValue || candidate.ProductId == productId))
                .Take(4)
                .ToList();
            if (exact.Count > 0)
            {
                return exact;
            }
        }

        return candidates
            .Where(candidate => candidate.Score >= 50)
            .Take(4)
            .ToList();
    }

    private static bool TryReadTarget(
        HidCandidate candidate,
        byte? preferredReportId,
        ProbeReport report,
        CancellationToken cancellationToken)
    {
        HidStream? stream = null;
        try
        {
            if (candidate.Device is null
                || !candidate.Device.TryOpen(out stream)
                || stream is null)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        try
        {
            stream.ReadTimeout = 300;
            stream.WriteTimeout = 300;

            var outLength = candidate.MaxOutputReportLength >= 37
                ? candidate.MaxOutputReportLength
                : 64;
            var inLength = candidate.MaxInputReportLength >= 37
                ? candidate.MaxInputReportLength
                : 64;

            var reportIds = new List<byte>();
            if (preferredReportId.HasValue)
            {
                reportIds.Add(preferredReportId.Value);
            }
            foreach (var fallback in new byte[] { 0x4B, 0x00 })
            {
                if (!reportIds.Contains(fallback))
                {
                    reportIds.Add(fallback);
                }
            }

            foreach (var reportId in reportIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var index = ReadIndex(stream, outLength, inLength, reportId);
                if (index is null)
                {
                    continue;
                }

                candidate.Responded = true;
                candidate.ActiveIndex = index;

                report.SelectedVendorId = candidate.VendorId;
                report.SelectedProductId = candidate.ProductId;
                report.SelectedProductName = candidate.DisplayName;
                report.ActiveIndex = index;

                var misses = 0;
                for (var band = 0; band < 16; band++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var payload = ReadBand(stream, outLength, inLength, reportId, (byte)band);
                    if (payload is null)
                    {
                        if (++misses >= 3)
                        {
                            break;
                        }
                        continue;
                    }

                    misses = 0;
                    report.Bands.Add(new BandProbe
                    {
                        Index = band,
                        Ok = true,
                        Length = payload.Length,
                        Hex = Convert.ToHexString(payload).ToLowerInvariant()
                    });
                }

                var preGain = ReadGain(stream, outLength, inLength, reportId, 0x23);
                var globalGain = ReadGain(stream, outLength, inLength, reportId, 0x03);
                report.PreGainDb = preGain is null ? null : preGain.Value / 256.0;
                report.GlobalGainDb = globalGain is null ? null : globalGain.Value / 256.0;
                report.Note = report.Bands.Count > 0
                    ? $"已读取 {report.Bands.Count} 段 PEQ 数据。"
                    : "设备有读取响应，但没有读到完整 PEQ 段。";
                return true;
            }
        }
        catch
        {
            // 探测失败不阻塞打包，继续尝试下一个候选
        }
        finally
        {
            stream.Dispose();
        }

        return false;
    }

    private static int? ReadIndex(HidStream stream, int outLength, int inLength, byte reportId)
    {
        var response = Query(stream, outLength, inLength, reportId, 0x0F, Array.Empty<byte>());
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response, reportId);
        return payload.Length >= 1 ? payload[0] : null;
    }

    private static byte[]? ReadBand(
        HidStream stream,
        int outLength,
        int inLength,
        byte reportId,
        byte band)
    {
        var response = Query(
            stream,
            outLength,
            inLength,
            reportId,
            0x09,
            new byte[] { 0x00, band });
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response, reportId);
        return payload.Length >= 33 ? payload[..33] : null;
    }

    private static short? ReadGain(
        HidStream stream,
        int outLength,
        int inLength,
        byte reportId,
        byte command)
    {
        var response = Query(stream, outLength, inLength, reportId, command, Array.Empty<byte>());
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response, reportId);
        return payload.Length >= 2 ? BitConverter.ToInt16(payload, 0) : null;
    }

    private static byte[]? Query(
        HidStream stream,
        int outLength,
        int inLength,
        byte reportId,
        byte command,
        byte[] payload)
    {
        var frame = new byte[outLength];
        frame[0] = reportId;
        frame[1] = 0x80;
        frame[2] = command;
        frame[3] = (byte)payload.Length;
        if (payload.Length > 0)
        {
            Array.Copy(payload, 0, frame, 4, payload.Length);
        }

        try
        {
            stream.Write(frame, 0, frame.Length);
            var buffer = new byte[inLength];
            var read = stream.Read(buffer, 0, buffer.Length);
            return read <= 0 ? null : buffer[..read];
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] Payload(byte[] response, byte reportId)
    {
        var offset = response.Length > 0 && response[0] == reportId ? 4 : 3;
        return response.Length <= offset ? Array.Empty<byte>() : response[offset..];
    }

    private static int SafeLength(Func<int> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeString(Func<string?> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ContainsMoondrop(string value)
    {
        return value.Contains("MOONDROP", StringComparison.OrdinalIgnoreCase)
            || value.Contains("水月雨", StringComparison.OrdinalIgnoreCase);
    }
}
