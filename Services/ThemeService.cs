using System.Windows;
using MoonDropEQ.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MoonDropEQ.Services;

public static class ThemeService
{
    private const string BrushDictionaryMarker = "Themes/App";

    public static AppSettings Current { get; private set; } = new();

    public static WindowBackdropType CurrentBackdrop => Current.Backdrop switch
    {
        BackdropMode.Acrylic => WindowBackdropType.Acrylic,
        BackdropMode.Solid => WindowBackdropType.None,
        _ => WindowBackdropType.Mica
    };

    public static void ApplyBackdrop(FluentWindow window)
    {
        try
        {
            window.WindowBackdropType = CurrentBackdrop;
        }
        catch
        {
            // 忽略个别窗口的材质设置失败
        }
    }

    public static void Initialize(AppSettings settings)
    {
        Current = settings;
        Apply(settings);
    }

    public static void Apply(AppSettings settings)
    {
        Current = settings;

        var theme = settings.Theme switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => ResolveSystemTheme()
        };

        var backdrop = CurrentBackdrop;

        try
        {
            ApplicationThemeManager.Apply(theme, backdrop, true);
        }
        catch
        {
            // 系统不支持该材质时保持现状
        }

        SwapBrushDictionary(theme == ApplicationTheme.Light);

        foreach (Window window in Application.Current.Windows)
        {
            if (window is not FluentWindow fluent)
            {
                continue;
            }

            try
            {
                fluent.WindowBackdropType = backdrop;
            }
            catch
            {
                // 单个窗口材质失败不影响其他窗口
            }
        }
    }

    private static ApplicationTheme ResolveSystemTheme()
    {
        try
        {
            var systemTheme = ApplicationThemeManager.GetSystemTheme();
            return systemTheme == ApplicationTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        }
        catch
        {
            return ApplicationTheme.Dark;
        }
    }

    private static void SwapBrushDictionary(bool light)
    {
        var uri = new Uri(
            $"pack://application:,,,/Themes/App{(light ? "Light" : "Dark")}.xaml",
            UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = null;
        foreach (var item in merged)
        {
            if (item.Source is not null && item.Source.OriginalString.Contains(BrushDictionaryMarker))
            {
                existing = item;
                break;
            }
        }

        var index = existing is null ? merged.Count : merged.IndexOf(existing);
        if (existing is not null)
        {
            merged.Remove(existing);
        }

        merged.Insert(Math.Min(index, merged.Count), dictionary);
    }
}
