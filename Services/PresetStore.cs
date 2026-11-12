using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public sealed class PresetFile
{
    public int Version { get; set; } = 1;

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

        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<PresetFile>(json, Options);
                if (data?.Presets is { Count: > 0 })
                {
                    foreach (var preset in data.Presets)
                    {
                        Presets.Add(preset);
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
        var data = new PresetFile { Version = 1, Presets = Presets.ToList() };
        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(FilePath, json);
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
        yield return new Preset { Id = "builtin-default", Name = "默认", Kind = PresetKind.BuiltIn, Index = 0, Hotkey = "Ctrl+Alt+1" };
        yield return new Preset { Id = "builtin-music-game", Name = "音乐游戏", Kind = PresetKind.BuiltIn, Index = 1, Hotkey = "Ctrl+Alt+2" };
        yield return new Preset { Id = "builtin-triple-a", Name = "3A 游戏", Kind = PresetKind.BuiltIn, Index = 2, Hotkey = "Ctrl+Alt+3" };
        yield return new Preset { Id = "builtin-fps1", Name = "FPS1", Kind = PresetKind.BuiltIn, Index = 3, Hotkey = "Ctrl+Alt+4" };
        yield return new Preset { Id = "builtin-fps2", Name = "FPS2", Kind = PresetKind.BuiltIn, Index = 4, Hotkey = "Ctrl+Alt+5" };
    }
}
