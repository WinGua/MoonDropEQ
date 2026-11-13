using System.Threading;
using MoonDropEQ.Core;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public static class PresetApplier
{
    public static void Apply(HidEqDevice device, Preset preset)
    {
        if (device.Profile.Capabilities.DualChannel)
        {
            throw new NotSupportedException("该设备需要双通道写入，当前版本暂不支持。");
        }

        if (preset.Kind == PresetKind.BuiltIn)
        {
            ApplyBuiltIn(device, (byte)Math.Clamp(preset.Index, 0, 255));
        }
        else
        {
            ApplyCurve(device, preset);
        }
    }

    private static void ApplyBuiltIn(HidEqDevice device, byte index)
    {
        if (!device.Profile.Capabilities.BuiltInSlots)
        {
            throw new InvalidOperationException("该设备没有内置预设槽位。");
        }

        device.WriteIndex(index);
        Thread.Sleep(device.Profile.Timing.SwitchDelayMs);
        if (device.Profile.Capabilities.PreGain)
        {
            device.WritePreGain(0);
        }
        if (device.Profile.Capabilities.GlobalGain)
        {
            device.WriteGlobalGain(0);
        }
        device.SaveFlash();
        Thread.Sleep(device.Profile.Timing.SaveDelayMs);
    }

    private static void ApplyCurve(HidEqDevice device, Preset preset)
    {
        if (!device.Profile.Capabilities.Curve)
        {
            throw new InvalidOperationException("该设备不支持自定义曲线。");
        }

        if (preset.Bands.Count < device.Profile.BandCount)
        {
            throw new InvalidOperationException($"预设曲线数据不完整（需要 {device.Profile.BandCount} 段）");
        }

        device.WriteIndex((byte)Math.Clamp(device.Profile.CurveSlot, 0, 255));
        Thread.Sleep(device.Profile.Timing.SwitchDelayMs);

        for (var index = 0; index < device.Profile.BandCount; index++)
        {
            device.WriteBand(Convert.FromHexString(preset.Bands[index]));
            Thread.Sleep(device.Profile.Timing.BandDelayMs);
        }

        device.CommitPeq();
        if (device.Profile.Capabilities.NeedsCommit)
        {
            Thread.Sleep(60);
        }
        if (device.Profile.Capabilities.PreGain)
        {
            device.WritePreGain(preset.PreGainDb);
        }
        if (device.Profile.Capabilities.GlobalGain)
        {
            device.WriteGlobalGain(preset.GlobalGainDb);
        }
        device.SaveFlash();
        Thread.Sleep(device.Profile.Timing.SaveDelayMs);
    }
}
