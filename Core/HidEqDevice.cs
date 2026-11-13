using System.IO;
using HidSharp;

namespace MoonDropEQ.Core;

/// <summary>
/// 按 DeviceProfile 工作的 HID EQ 设备。
/// 帧格式：[reportId][op][cmd][len][payload...]，输出报告长度由设备描述决定。
/// </summary>
public sealed class HidEqDevice : IDisposable
{
    private readonly HidStream _stream;
    private readonly int _outLength;
    private readonly int _inLength;

    private HidEqDevice(DeviceProfile profile, HidStream stream, int outLength, int inLength)
    {
        Profile = profile;
        _stream = stream;
        _outLength = outLength;
        _inLength = inLength;
    }

    public DeviceProfile Profile { get; }

    public static HidEqDevice? TryOpen(DeviceProfile profile, HidDevice device)
    {
        HidStream? stream = null;
        try
        {
            if (!device.TryOpen(out stream) || stream is null)
            {
                return null;
            }

            stream.ReadTimeout = 600;
            stream.WriteTimeout = 600;

            var outLength = device.GetMaxOutputReportLength();
            var inLength = device.GetMaxInputReportLength();
            var minimumLength = Math.Max(64, profile.BandPayloadSize + 4);
            if (outLength < minimumLength)
            {
                outLength = minimumLength;
            }
            if (inLength < minimumLength)
            {
                inLength = minimumLength;
            }

            var result = new HidEqDevice(profile, stream, outLength, inLength);
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

        return null;
    }

    public int? ReadIndex()
    {
        var response = Query(Profile.Commands.EqIndexValue, Array.Empty<byte>());
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response);
        return payload.Length >= 1 ? payload[0] : null;
    }

    public string[]? ReadAllBands()
    {
        var result = new string[Profile.BandCount];
        for (var index = 0; index < Profile.BandCount; index++)
        {
            var payload = ReadBand(index);
            if (payload is null)
            {
                return null;
            }
            result[index] = Convert.ToHexString(payload).ToLowerInvariant();
        }
        return result;
    }

    public byte[]? ReadBand(int band)
    {
        var response = Query(Profile.Commands.EqBandValue, new byte[] { 0x00, (byte)band });
        if (response is null)
        {
            return null;
        }

        var payload = Payload(response);
        return payload.Length < Profile.BandPayloadSize ? null : payload[..Profile.BandPayloadSize];
    }

    public double ReadPreGainDb()
    {
        return Profile.Capabilities.PreGain
            ? ReadInt16(Profile.Commands.PreGainValue) / Profile.GainScale
            : 0;
    }

    public double ReadGlobalGainDb()
    {
        return Profile.Capabilities.GlobalGain
            ? ReadInt16(Profile.Commands.GlobalGainValue) / Profile.GainScale
            : 0;
    }

    public void WriteIndex(byte index)
    {
        Send(Profile.Commands.EqIndexValue, new[] { index });
    }

    public void WriteBand(byte[] payload)
    {
        if (payload.Length != Profile.BandPayloadSize)
        {
            throw new ArgumentException($"PEQ 段载荷必须是 {Profile.BandPayloadSize} 字节", nameof(payload));
        }
        Send(Profile.Commands.EqBandValue, payload);
    }

    public void CommitPeq()
    {
        if (!Profile.Capabilities.NeedsCommit)
        {
            return;
        }

        var command = Profile.Commands.CommitValue;
        if (command is null)
        {
            return;
        }

        Send(command.Value, new byte[] { 0x00, 0x00, 0xFF, 0xFF });
    }

    public void WritePreGain(double db)
    {
        if (!Profile.Capabilities.PreGain)
        {
            return;
        }

        var command = Profile.Commands.PreGainValue;
        if (command is not null)
        {
            Send(command.Value, Int16Bytes(ToFixed(db)));
        }
    }

    public void WriteGlobalGain(double db)
    {
        if (!Profile.Capabilities.GlobalGain)
        {
            return;
        }

        var command = Profile.Commands.GlobalGainValue;
        if (command is not null)
        {
            Send(command.Value, Int16Bytes(ToFixed(db)));
        }
    }

    public void SaveFlash()
    {
        Send(Profile.Commands.SaveValue, Array.Empty<byte>());
    }

    private short ReadInt16(byte? command)
    {
        if (command is null)
        {
            return 0;
        }

        var response = Query(command.Value, Array.Empty<byte>());
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

    private void Send(byte command, byte[] payload)
    {
        var frame = new byte[_outLength];
        frame[0] = Profile.ReportIdValue;
        frame[1] = Profile.WriteOpValue;
        frame[2] = command;
        frame[3] = (byte)payload.Length;
        if (payload.Length > 0)
        {
            Array.Copy(payload, 0, frame, 4, payload.Length);
        }
        _stream.Write(frame, 0, frame.Length);
    }

    private byte[]? Query(byte command, byte[] payload)
    {
        var frame = new byte[_outLength];
        frame[0] = Profile.ReportIdValue;
        frame[1] = Profile.ReadOpValue;
        frame[2] = command;
        frame[3] = (byte)payload.Length;
        if (payload.Length > 0)
        {
            Array.Copy(payload, 0, frame, 4, payload.Length);
        }

        try
        {
            _stream.Write(frame, 0, frame.Length);
            var buffer = new byte[_inLength];
            var read = _stream.Read(buffer, 0, buffer.Length);
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
    }

    private byte[] Payload(byte[] response)
    {
        var offset = response.Length > 0 && response[0] == Profile.ReportIdValue ? 4 : 3;
        return response.Length <= offset ? Array.Empty<byte>() : response[offset..];
    }

    private short ToFixed(double value)
    {
        var scaled = Math.Round(value * Profile.GainScale);
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
