using System.Threading;
using MoonDropEQ.Core;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public static class PresetApplier
{
    public static void Apply(RaysDevice device, Preset preset)
    {
        if (preset.Kind == PresetKind.BuiltIn)
        {
            ApplyBuiltIn(device, (byte)preset.Index);
        }
        else
        {
            ApplyCurve(device, preset);
        }
    }

    private static void ApplyBuiltIn(RaysDevice device, byte index)
    {
        device.WriteIndex(index);
        Thread.Sleep(150);
        device.WritePreGain(0);
        device.WriteGlobalGain(0);
        device.SaveFlash();
        Thread.Sleep(300);
    }

    private static void ApplyCurve(RaysDevice device, Preset preset)
    {
        if (preset.Bands.Count < RaysDevice.BandCount)
        {
            throw new InvalidOperationException("预设曲线数据不完整（需要 8 段）");
        }

        device.WriteIndex(RaysDevice.PeqSlot);
        Thread.Sleep(150);

        for (var i = 0; i < RaysDevice.BandCount; i++)
        {
            device.WriteBand(Convert.FromHexString(preset.Bands[i]));
            Thread.Sleep(30);
        }

        device.CommitPeq();
        Thread.Sleep(60);
        device.WritePreGain(preset.PreGainDb);
        device.WriteGlobalGain(preset.GlobalGainDb);
        device.SaveFlash();
        Thread.Sleep(300);
    }
}
