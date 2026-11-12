using System.Windows;
using MoonDropEQ.Models;
using MoonDropEQ.Services;
using Wpf.Ui.Controls;

namespace MoonDropEQ;

public partial class SettingsWindow : FluentWindow
{
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeService.ApplyBackdrop(this);

        _loading = true;
        var settings = ThemeService.Current;

        CloseToTrayRadio.IsChecked = settings.CloseToTray;
        ExitRadio.IsChecked = !settings.CloseToTray;

        MicaRadio.IsChecked = settings.Backdrop == BackdropMode.Mica;
        AcrylicRadio.IsChecked = settings.Backdrop == BackdropMode.Acrylic;
        SolidRadio.IsChecked = settings.Backdrop == BackdropMode.Solid;

        SystemThemeRadio.IsChecked = settings.Theme == ThemeMode.System;
        DarkThemeRadio.IsChecked = settings.Theme == ThemeMode.Dark;
        LightThemeRadio.IsChecked = settings.Theme == ThemeMode.Light;

        _loading = false;
    }

    private void UpdateSettings(Action<AppSettings> change)
    {
        if (_loading)
        {
            return;
        }

        var settings = ThemeService.Current;
        change(settings);
        ThemeService.Apply(settings);
        SettingsStore.Save(settings);
    }

    private void CloseToTray_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.CloseToTray = true);
    }

    private void Exit_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.CloseToTray = false);
    }

    private void Mica_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Backdrop = BackdropMode.Mica);
    }

    private void Acrylic_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Backdrop = BackdropMode.Acrylic);
    }

    private void Solid_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Backdrop = BackdropMode.Solid);
    }

    private void SystemTheme_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Theme = ThemeMode.System);
    }

    private void DarkTheme_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Theme = ThemeMode.Dark);
    }

    private void LightTheme_Checked(object sender, RoutedEventArgs e)
    {
        UpdateSettings(settings => settings.Theme = ThemeMode.Light);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
