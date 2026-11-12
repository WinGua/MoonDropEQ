using System.Windows;
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

    public event Action? AboutRequested;

    public event Action<Preset>? PresetRequested;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "MoonDropEQ",
            Icon = CreateIcon(),
            ContextMenu = new ContextMenu { Style = MenuStyle("AppContextMenuStyle") }
        };
        _icon.TrayLeftMouseUp += (_, _) => ShowRequested?.Invoke();
    }

    public void Update(IEnumerable<Preset> presets)
    {
        var menu = new ContextMenu { Style = MenuStyle("AppContextMenuStyle") };

        foreach (var preset in presets)
        {
            var item = new MenuItem
            {
                Header = preset.Name,
                InputGestureText = preset.Hotkey,
                Style = MenuStyle("AppMenuItemStyle")
            };
            var captured = preset;
            item.Click += (_, _) => PresetRequested?.Invoke(captured);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator { Style = MenuStyle("AppMenuSeparatorStyle") });

        var show = new MenuItem { Header = "显示窗口", Style = MenuStyle("AppMenuItemStyle") };
        show.Click += (_, _) => ShowRequested?.Invoke();
        menu.Items.Add(show);

        var about = new MenuItem { Header = "关于", Style = MenuStyle("AppMenuItemStyle") };
        about.Click += (_, _) => AboutRequested?.Invoke();
        menu.Items.Add(about);

        var exit = new MenuItem { Header = "退出", Style = MenuStyle("AppMenuItemStyle") };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exit);

        _icon.ContextMenu = menu;

        if (!_created)
        {
            _icon.ForceCreate();
            _created = true;
        }
    }

    private static Style? MenuStyle(string key)
    {
        return Application.Current?.TryFindResource(key) as Style;
    }

    private static System.Drawing.Icon CreateIcon()
    {
        var assembly = typeof(TrayService).Assembly;
        using var stream = assembly.GetManifestResourceStream("MoonDropEQ.app.ico");
        if (stream is not null)
        {
            return new System.Drawing.Icon(stream, new System.Drawing.Size(32, 32));
        }

        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(76, 194, 255));
            graphics.FillEllipse(brush, 5, 5, 22, 22);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
