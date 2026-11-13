using System.Windows;
using MoonDropEQ.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MoonDropEQ.Services;

public static class ThemeService
{
    private const string BrushDictionaryMarker = "Themes/App";

    private static double _lastAppliedScale = double.NaN;

    public static AppSettings Current { get; private set; } = new();

    public static WindowBackdropType CurrentBackdrop => Current.Backdrop switch
    {
        BackdropMode.Acrylic => WindowBackdropType.Acrylic,
        BackdropMode.Solid => WindowBackdropType.None,
        _ => WindowBackdropType.Mica
    };

    public static double UiScale
    {
        get
        {
            var percent = Current.UiScalePercent switch
            {
                90 => 90,
                110 => 110,
                125 => 125,
                _ => 100
            };
            return percent / 100.0;
        }
    }

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
        var scale = UiScale;
        var scaleChanged = double.IsNaN(_lastAppliedScale)
            || Math.Abs(_lastAppliedScale - scale) > 0.001;
        _lastAppliedScale = scale;

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
            if (scaleChanged && window is IUiScalable scalable)
            {
                try
                {
                    scalable.ApplyUiScale(scale);
                }
                catch
                {
                    // 单个窗口缩放失败不影响其他窗口
                }
            }

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
            return systemTheme switch
            {
                SystemTheme.Light => ApplicationTheme.Light,
                SystemTheme.Dark => ApplicationTheme.Dark,
                _ => ApplicationTheme.Dark
            };
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

public interface IUiScalable
{
    void ApplyUiScale(double scale);
}
