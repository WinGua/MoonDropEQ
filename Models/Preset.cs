namespace MoonDropEQ.Models;

public enum PresetKind
{
    /// <summary>耳机固件内置的场景预设，按索引切换。</summary>
    BuiltIn,

    /// <summary>自定义曲线，需要写入 PEQ 工作区。</summary>
    Curve
}

public sealed class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public PresetKind Kind { get; set; } = PresetKind.BuiltIn;

    /// <summary>预设所属的设备描述 Id。</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>内置预设对应的设备索引。</summary>
    public int Index { get; set; }

    public double PreGainDb { get; set; }

    public double GlobalGainDb { get; set; }

    /// <summary>PEQ 原始载荷，段数与单段字节数由设备描述决定。</summary>
    public List<string> Bands { get; set; } = new();

    public string Hotkey { get; set; } = string.Empty;

    public Preset Clone()
    {
        return new Preset
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            DeviceId = DeviceId,
            Index = Index,
            PreGainDb = PreGainDb,
            GlobalGainDb = GlobalGainDb,
            Bands = new List<string>(Bands),
            Hotkey = Hotkey
        };
    }
}
