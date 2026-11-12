using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MoonDropEQ",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch
        {
            // 配置损坏时使用默认值
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 写入失败时静默忽略，不影响主流程
        }
    }
}
