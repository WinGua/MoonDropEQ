using System.Windows.Controls;
using H.NotifyIcon;
using MoonDropEQ.Models;

namespace MoonDropEQ.Services;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private bool _created;

    public event Action? ShowRequested;

    public event Action? ExitRequested;

    public event Action<Preset>? PresetRequested;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "MoonDropEQ",
            Icon = CreateIcon(),
            ContextMenu = new ContextMenu()
        };
        _icon.TrayLeftMouseUp += (_, _) => ShowRequested?.Invoke();
    }

    public void Update(IEnumerable<Preset> presets)
    {
        var menu = new ContextMenu();

        foreach (var preset in presets)
        {
            var header = string.IsNullOrWhiteSpace(preset.Hotkey)
                ? preset.Name
                : $"{preset.Name}    {preset.Hotkey}";
            var item = new MenuItem { Header = header };
            var captured = preset;
            item.Click += (_, _) => PresetRequested?.Invoke(captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var show = new MenuItem { Header = "显示窗口" };
        show.Click += (_, _) => ShowRequested?.Invoke();
        menu.Items.Add(show);

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        _icon.ContextMenu = menu;

        if (!_created)
        {
            _icon.ForceCreate();
            _created = true;
        }
    }

    private static System.Drawing.Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(76, 194, 255));
            graphics.FillEllipse(brush, 5, 5, 22, 22);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 240, 255), 2.4f);
            graphics.DrawArc(pen, 9, 12, 14, 10, 200, 150);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
