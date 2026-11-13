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
                            // v1 预设来自仅支持 Rays 的版本
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
                // 文件损坏时保持空列表，稍后按设备描述生成默认项
            }
        }

        // 没有任何预设时保持空列表，由 MainWindow 按当前设备描述生成默认项。
    }

    public void Save()
    {
        var data = new PresetFile { Version = 2, Presets = Presets.ToList() };
        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(FilePath, json);
    }

    public void EnsureDefaultsForDevice(DeviceProfile profile)
    {
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
}
