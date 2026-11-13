using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace MoonDropEQ.Core;

public sealed class DeviceProfile
{
    public int Schema { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string VendorId { get; set; } = "0x0000";

    public List<string> ProductIds { get; set; } = new();

    public string ReportId { get; set; } = "0x00";

    public string ReadOp { get; set; } = "0x80";

    public string WriteOp { get; set; } = "0x01";

    public int BandCount { get; set; } = 8;

    public int BandPayloadSize { get; set; } = 33;

    public int CurveSlot { get; set; } = 7;

    public double GainScale { get; set; } = 256;

    public List<SlotProfile> Slots { get; set; } = new();

    public CommandProfile Commands { get; set; } = new();

    public CapabilityProfile Capabilities { get; set; } = new();

    public TimingProfile Timing { get; set; } = new();

    public VerifiedProfile? Verified { get; set; }

    public string? Source { get; set; }

    public string? Notes { get; set; }

    [JsonIgnore]
    public int VendorIdValue => ParseHex(VendorId);

    [JsonIgnore]
    public IReadOnlyList<int> ProductIdValues => ProductIds.Select(ParseHex).ToList();

    [JsonIgnore]
    public byte ReportIdValue => ToByte(ParseHex(ReportId));

    [JsonIgnore]
    public byte ReadOpValue => ToByte(ParseHex(ReadOp));

    [JsonIgnore]
    public byte WriteOpValue => ToByte(ParseHex(WriteOp));

    [JsonIgnore]
    public bool IsValid =>
        Schema == 1
        && !string.IsNullOrWhiteSpace(Id)
        && !string.IsNullOrWhiteSpace(Name)
        && ParseHex(VendorId) >= 0
        && ProductIds.Count > 0
        && ProductIds.All(productId => ParseHex(productId) >= 0)
        && ParseHex(ReportId) >= 0
        && ParseHex(ReadOp) >= 0
        && ParseHex(WriteOp) >= 0
        && BandCount > 0
        && BandPayloadSize > 0
        && Commands.IsValid;

    public bool Matches(int vendorId, int productId)
    {
        return ParseHex(VendorId) == vendorId
            && ProductIds.Any(item => ParseHex(item) == productId);
    }

    public static int ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return -1;
        }

        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(
            text,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : -1;
    }

    public static byte ToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }
}

public sealed class SlotProfile
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class CommandProfile
{
    public string EqIndex { get; set; } = string.Empty;

    public string EqBand { get; set; } = string.Empty;

    public string? Commit { get; set; }

    public string? PreGain { get; set; }

    public string? GlobalGain { get; set; }

    public string Save { get; set; } = string.Empty;

    [JsonIgnore]
    public byte EqIndexValue => DeviceProfile.ToByte(DeviceProfile.ParseHex(EqIndex));

    [JsonIgnore]
    public byte EqBandValue => DeviceProfile.ToByte(DeviceProfile.ParseHex(EqBand));

    [JsonIgnore]
    public byte? CommitValue => OptionalValue(Commit);

    [JsonIgnore]
    public byte? PreGainValue => OptionalValue(PreGain);

    [JsonIgnore]
    public byte? GlobalGainValue => OptionalValue(GlobalGain);

    [JsonIgnore]
    public byte SaveValue => DeviceProfile.ToByte(DeviceProfile.ParseHex(Save));

    [JsonIgnore]
    public bool IsValid =>
        DeviceProfile.ParseHex(EqIndex) >= 0
        && DeviceProfile.ParseHex(EqBand) >= 0
        && DeviceProfile.ParseHex(Save) >= 0;

    private static byte? OptionalValue(string? value)
    {
        var parsed = DeviceProfile.ParseHex(value);
        return parsed < 0 ? null : DeviceProfile.ToByte(parsed);
    }
}

public sealed class CapabilityProfile
{
    public bool BuiltInSlots { get; set; }

    public bool Curve { get; set; }

    public bool PreGain { get; set; }

    public bool GlobalGain { get; set; }

    public bool NeedsCommit { get; set; }

    public bool DualChannel { get; set; }
}

public sealed class TimingProfile
{
    public int SwitchDelayMs { get; set; } = 150;

    public int BandDelayMs { get; set; } = 30;

    public int SaveDelayMs { get; set; } = 300;
}

public sealed class VerifiedProfile
{
    public string Level { get; set; } = "experimental";

    public string? Notes { get; set; }
}
