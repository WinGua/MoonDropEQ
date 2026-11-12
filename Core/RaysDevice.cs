using System.IO;
using HidSharp;

namespace MoonDropEQ.Core;

/// <summary>
/// MOONDROP Rays (VID 0x35D8 / PID 0x011B) 的 HID 通信封装。
/// 帧格式：[reportId][op][cmd][len][payload...]，输出报告固定 64 字节。
/// </summary>
public sealed class RaysDevice : IDisposable
{
    public const int VendorId = 0x35D8;
    public const int ProductId = 0x011B;
    public const byte ReportId = 0x4B;

    private const byte OpRead = 0x80;
    private const byte OpWrite = 0x01;
    private const byte CmdEqIndex = 0x0F;
    private const byte CmdEqBand = 0x09;
    private const byte CmdPreGain = 0x23;
    private const byte CmdGlobalGain = 0x03;
    private const byte CmdSaveFlash = 0x01;
    private const byte CmdCommit = 0x0A;

    public const int BandCount = 8;
    public const int BandPayloadSize = 33;
    public const byte PeqSlot = 7;

    private readonly HidStream _stream;
    private readonly int _outLength;
    private readonly int _inLength;

    private RaysDevice(HidStream stream, int outLength, int inLength)
    {
        _stream = stream;
        _outLength = outLength;
        _inLength = inLength;
    }

    /// <summary>尝试打开耳机；不是目标接口时返回 null。</summary>
    public static RaysDevice? TryOpen()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(VendorId, ProductId))
        {
            HidStream? stream = null;
            try
            {
                if (!device.TryOpen(out stream))
                {
                    continue;
                }

                stream.ReadTimeout = 600;
                stream.WriteTimeout = 600;

                var outLength = device.GetMaxOutputReportLength();
                var inLength = device.GetMaxInputReportLength();
                if (outLength < BandPayloadSize + 4)
                {
                    outLength = 64;
                }
                if (inLength < 64)
                {
                    inLength = 64;
                }

                var result = new RaysDevice(stream, outLength, inLength);
                if (result.ReadIndex() is not null)
                {
                    return result;
                }

                result.Dispose();
            }
            catch
            {
                stream?.Dispose();
            }
        }

        return null;
    }

    public int? ReadIndex()
    {
        var response = Query(OpRead, CmdEqIndex, Array.Empty<byte>());
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response);
        return payload.Length >= 1 ? payload[0] : null;
    }

    public string[]? ReadAllBands()
    {
        var result = new string[BandCount];
        for (var i = 0; i < BandCount; i++)
        {
            var payload = ReadBand(i);
            if (payload is null)
            {
                return null;
            }
            result[i] = Convert.ToHexString(payload).ToLowerInvariant();
        }
        return result;
    }

    public byte[]? ReadBand(int band)
    {
        var response = Query(OpRead, CmdEqBand, new byte[] { 0x00, (byte)band });
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response);
        if (payload.Length < BandPayloadSize)
        {
            return null;
        }

        return payload[..BandPayloadSize];
    }

    public double ReadPreGainDb()
    {
        var value = ReadInt16(CmdPreGain);
        return value / 256.0;
    }

    public double ReadGlobalGainDb()
    {
        var value = ReadInt16(CmdGlobalGain);
        return value / 256.0;
    }

    public void WriteIndex(byte index)
    {
        Send(OpWrite, CmdEqIndex, new[] { index });
    }

    public void WriteBand(byte[] payload)
    {
        if (payload.Length != BandPayloadSize)
        {
            throw new ArgumentException($"PEQ 段载荷必须是 {BandPayloadSize} 字节", nameof(payload));
        }
        Send(OpWrite, CmdEqBand, payload);
    }

    public void CommitPeq()
    {
        Send(OpWrite, CmdCommit, new byte[] { 0x00, 0x00, 0xFF, 0xFF });
    }

    public void WritePreGain(double db)
    {
        Send(OpWrite, CmdPreGain, Int16Bytes(ToFixed(db)));
    }

    public void WriteGlobalGain(double db)
    {
        Send(OpWrite, CmdGlobalGain, Int16Bytes(ToFixed(db)));
    }

    public void SaveFlash()
    {
        Send(OpWrite, CmdSaveFlash, Array.Empty<byte>());
    }

    private int ReadInt16(byte command)
    {
        var response = Query(OpRead, command, Array.Empty<byte>());
        if (response is null)
        {
            return 0;
        }

        var payload = Payload(response);
        if (payload.Length < 2)
        {
            return 0;
        }

        return BitConverter.ToInt16(payload, 0);
    }

    private void Send(byte op, byte command, byte[] payload)
    {
        var frame = new byte[_outLength];
        frame[0] = ReportId;
        frame[1] = op;
        frame[2] = command;
        frame[3] = (byte)payload.Length;
        Array.Copy(payload, 0, frame, 4, payload.Length);
        _stream.Write(frame, 0, frame.Length);
    }

    private byte[]? Query(byte op, byte command, byte[] payload)
    {
        Send(op, command, payload);

        var buffer = new byte[_inLength];
        try
        {
            var read = _stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return null;
            }
            return buffer[..read];
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>去掉报告号、op、cmd、len 四个字节，取出载荷。</summary>
    private static byte[] Payload(byte[] response)
    {
        var offset = response.Length > 0 && response[0] == ReportId ? 4 : 3;
        if (response.Length <= offset)
        {
            return Array.Empty<byte>();
        }
        return response[offset..];
    }

    private static short ToFixed(double value)
    {
        var scaled = Math.Round(value * 256.0);
        return (short)Math.Clamp(scaled, -32768, 32767);
    }

    private static byte[] Int16Bytes(short value)
    {
        return BitConverter.GetBytes(value);
    }

    public void Dispose()
    {
        try
        {
            _stream.Dispose();
        }
        catch
        {
            // 忽略关闭异常
        }
    }
}
