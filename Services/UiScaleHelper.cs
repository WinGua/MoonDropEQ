using System.Windows;
using System.Windows.Media;

namespace MoonDropEQ.Services;

public static class UiScaleHelper
{
    public static void Apply(Window window, double baseWidth, double? baseHeight = null)
    {
        var scale = ThemeService.UiScale;

        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = new ScaleTransform(scale, scale);
        }

        window.Width = baseWidth * scale;

        if (baseHeight.HasValue)
        {
            window.Height = baseHeight.Value * scale;
        }
    }
}
