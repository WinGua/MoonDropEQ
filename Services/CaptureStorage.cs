using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MoonDropEQ.Services;

/// <summary>设备适配采集包的存放与打包。</summary>
public static class CaptureStorage
{
    public static string RootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MoonDropEQ",
        "adapt");

    public static void EnsureDirectory()
    {
        Directory.CreateDirectory(RootPath);
    }

    public static IReadOnlyList<FileInfo> ListPackages()
    {
        EnsureDirectory();
        return new DirectoryInfo(RootPath)
            .GetFiles("MoonDropEQ-adapt-*.zip")
            .OrderByDescending(file => file.LastWriteTime)
            .ToList();
    }

    public static FileInfo? LatestPackage()
    {
        return ListPackages().FirstOrDefault();
    }

    public static string CreatePackage(string fileName, IEnumerable<(string Name, string Content)> entries)
    {
        EnsureDirectory();
        var safeName = Path.GetFileName(fileName);
        var zipPath = Path.Combine(RootPath, safeName);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return zipPath;
    }

    public static void OpenFolder()
    {
        EnsureDirectory();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{RootPath}\"")
        {
            UseShellExecute = true
        });
    }
}
