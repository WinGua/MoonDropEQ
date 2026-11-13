using System.IO;
using System.Linq;
using System.Text.Json;
using HidSharp;

namespace MoonDropEQ.Core;

public static class DeviceRegistry
{
    private const string ResourceName = "MoonDropEQ.Core.devices.json";

    private static readonly Lazy<List<DeviceProfile>> Profiles = new(LoadProfiles);

    public static IReadOnlyList<DeviceProfile> All => Profiles.Value;

    public static DeviceProfile? DefaultProfile => Profiles.Value.FirstOrDefault();

    public static DeviceProfile? Match(int vendorId, int productId)
    {
        return Profiles.Value.FirstOrDefault(profile => profile.Matches(vendorId, productId));
    }

    public static HidEqDevice? TryOpen()
    {
        foreach (var profile in Profiles.Value)
        {
            foreach (var productId in profile.ProductIdValues)
            {
                foreach (var device in DeviceList.Local.GetHidDevices(profile.VendorIdValue, productId))
                {
                    var opened = HidEqDevice.TryOpen(profile, device);
                    if (opened is not null)
                    {
                        return opened;
                    }
                }
            }
        }

        return null;
    }

    private static List<DeviceProfile> LoadProfiles()
    {
        try
        {
            using var stream = typeof(DeviceRegistry).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return new List<DeviceProfile>();
            }

            using var reader = new StreamReader(stream);
            var profiles = JsonSerializer.Deserialize<List<DeviceProfile>>(
                reader.ReadToEnd(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<DeviceProfile>();

            foreach (var profile in profiles)
            {
                profile.ProductIds ??= new List<string>();
                profile.Slots ??= new List<SlotProfile>();
                profile.Commands ??= new CommandProfile();
                profile.Capabilities ??= new CapabilityProfile();
                profile.Timing ??= new TimingProfile();
            }

            return profiles.Where(profile => profile.IsValid).ToList();
        }
        catch
        {
            return new List<DeviceProfile>();
        }
    }
}
