using System.Windows;
using System.Windows.Media;

namespace MoonDropEQ.Services;

public static class UiScaleHelper
{
    public static void Apply(Window window, double baseWidth)
    {
        var scale = ThemeService.UiScale;
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(320, workArea.Width - 40);
        var maxHeight = Math.Max(240, workArea.Height - 40);

        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = new ScaleTransform(scale, scale);
        }

        window.MaxWidth = maxWidth;
        window.MaxHeight = maxHeight;
        window.Width = Math.Min(baseWidth * scale, maxWidth);
    }
}
