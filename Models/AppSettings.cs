namespace MoonDropEQ.Models;

public enum ThemeMode
{
    System,
    Dark,
    Light
}

public enum BackdropMode
{
    Mica,
    Acrylic,
    Solid
}

public sealed class AppSettings
{
    /// <summary>关闭窗口时收进托盘（false 表示直接退出）。</summary>
    public bool CloseToTray { get; set; } = true;

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    public BackdropMode Backdrop { get; set; } = BackdropMode.Mica;

    /// <summary>界面缩放百分比，支持 90 / 100 / 110 / 125。</summary>
    public int UiScalePercent { get; set; } = 100;
}
