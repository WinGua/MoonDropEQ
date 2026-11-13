using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using MoonDropEQ.Core;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public sealed class PresetFile
{
    public int Version { get; set; } = 2;

    public List<Preset> Presets { get; set; } = new();
}

public sealed class PresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; }

    public ObservableCollection<Preset> Presets { get; } = new();

    public PresetStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MoonDropEQ");
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "presets.json");
    }

    public void Load()
    {
        Presets.Clear();
        var needsSave = false;

        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<PresetFile>(json, Options);
                if (data?.Presets is { Count: > 0 })
                {
                    if (data.Version < 2)
                    {
                        needsSave = true;
                    }

                    foreach (var preset in data.Presets)
                    {
                        if (string.IsNullOrWhiteSpace(preset.DeviceId))
                        {
                            preset.DeviceId = "rays";
                            needsSave = true;
                        }
                        Presets.Add(preset);
                    }

                    if (needsSave)
                    {
                        Save();
                    }
                    return;
                }
            }
            catch
            {
                // 文件损坏时回落到默认列表
            }
        }

        foreach (var preset in CreateDefaults())
        {
            Presets.Add(preset);
        }
        Save();
    }

    public void Save()
    {
        var data = new PresetFile { Version = 2, Presets = Presets.ToList() };
        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(FilePath, json);
    }

    public void EnsureDefaultsForDevice(DeviceProfile profile)
    {
        if (string.Equals(profile.Id, "rays", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!profile.Capabilities.BuiltInSlots || profile.Slots.Count == 0)
        {
            return;
        }

        if (Presets.Any(preset =>
                string.Equals(preset.DeviceId, profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        foreach (var slot in profile.Slots.OrderBy(slot => slot.Index))
        {
            Presets.Add(new Preset
            {
                Id = $"{profile.Id}-builtin-{slot.Index}",
                Name = slot.Name,
                Kind = PresetKind.BuiltIn,
                DeviceId = profile.Id,
                Index = slot.Index,
                Hotkey = NextHotkey()
            });
        }
        Save();
    }

    public string NextHotkey()
    {
        for (var i = 1; i <= 9; i++)
        {
            var candidate = $"Ctrl+Alt+{i}";
            if (Presets.All(p => !string.Equals(p.Hotkey, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
        return string.Empty;
    }

    private static IEnumerable<Preset> CreateDefaults()
    {
        yield return new Preset { Id = "builtin-default", Name = "默认", Kind = PresetKind.BuiltIn, DeviceId = "rays", Index = 0, Hotkey = "Ctrl+Alt+1" };
        yield return new Preset { Id = "builtin-music-game", Name = "音乐游戏", Kind = PresetKind.BuiltIn, DeviceId = "rays", Index = 1, Hotkey = "Ctrl+Alt+2" };
        yield return new Preset { Id = "builtin-triple-a", Name = "3A 游戏", Kind = PresetKind.BuiltIn, DeviceId = "rays", Index = 2, Hotkey = "Ctrl+Alt+3" };
        yield return new Preset { Id = "builtin-fps1", Name = "FPS1", Kind = PresetKind.BuiltIn, DeviceId = "rays", Index = 3, Hotkey = "Ctrl+Alt+4" };
        yield return new Preset { Id = "builtin-fps2", Name = "FPS2", Kind = PresetKind.BuiltIn, DeviceId = "rays", Index = 4, Hotkey = "Ctrl+Alt+5" };
    }
}
