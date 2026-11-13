using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MoonDropEQ.Core;
using MoonDropEQ.Models;
using MoonDropEQ.Services;
using Wpf.Ui.Controls;

namespace MoonDropEQ;

public partial class MainWindow : FluentWindow
{
    private readonly PresetStore _store = new();
    private readonly ObservableCollection<PresetItem> _items = new();
    private readonly DispatcherTimer _toastTimer;

    private HotkeyService? _hotkeys;
    private TrayService? _tray;
    private HidEqDevice? _device;
    private string _activeDeviceId = "rays";
    private Preset? _lastCurve;
    private Preset? _activePreset;
    private bool _busy;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        ThemeService.ApplyBackdrop(this);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        _store.Load();
        _activeDeviceId = DeviceRegistry.DefaultProfile?.Id ?? "rays";
        CaptureButton.IsEnabled = DeviceRegistry.DefaultProfile?.Capabilities.Curve ?? true;
        PresetList.ItemsSource = _items;
        RefreshItems();
        SettingsPage.OpenPresetFileRequested += (_, _) => OpenPresetFile();

        SourceInitialized += (_, _) => SetupInfrastructure();
        Loaded += async (_, _) => await DetectDeviceAsync();
        Closing += OnWindowClosing;
    }

    private void SetupInfrastructure()
    {
        _hotkeys = new HotkeyService(this);

        _tray = new TrayService();
        _tray.ShowRequested += ShowFromTray;
        _tray.ExitRequested += ExitApplication;
        _tray.AboutRequested += () => ShowPage(AboutHost);
        _tray.PresetRequested += preset => _ = ApplyPresetAsync(preset);
        _tray.Update(ActivePresets);

        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null)
        {
            return;
        }

        _hotkeys.UnregisterAll();
        var failures = new List<string>();

        foreach (var preset in ActivePresets)
        {
            if (string.IsNullOrWhiteSpace(preset.Hotkey))
            {
                continue;
            }

            var captured = preset;
            if (!_hotkeys.TryRegister(captured.Hotkey, () => _ = ApplyPresetAsync(captured), out var error))
            {
                failures.Add($"{captured.Name}（{error}）");
            }
        }

        if (failures.Count > 0)
        {
            ShowToast("部分热键未生效：" + string.Join("、", failures), true);
        }
    }

    private async Task DetectDeviceAsync()
    {
        UpdateConnection(null, "正在查找耳机…");

        var found = await Task.Run(DeviceRegistry.TryOpen);
        if (found is null)
        {
            UpdateConnection(false, "未连接（插上耳机后重试一次应用）");
            return;
        }

        _device = found;
        ApplyProfile(found.Profile);
        UpdateConnection(true, $"已连接 · {found.Profile.Name}");
        await RefreshActiveAsync();
    }

    private HidEqDevice? EnsureDevice()
    {
        if (_device is not null)
        {
            return _device;
        }

        _device = DeviceRegistry.TryOpen();
        if (_device is not null)
        {
            ApplyProfile(_device.Profile);
        }

        UpdateConnection(
            _device is not null,
            _device is null ? "未连接" : $"已连接 · {_device.Profile.Name}");
        return _device;
    }

    private async Task RefreshActiveAsync()
    {
        var device = _device;
        if (device is null)
        {
            return;
        }

        var index = await Task.Run(device.ReadIndex);
        if (index is null)
        {
            return;
        }

        Preset? active = null;
        if (index == device.Profile.CurveSlot)
        {
            active = _lastCurve;
        }
        else
        {
            active = _store.Presets.FirstOrDefault(p =>
                p.Kind == PresetKind.BuiltIn
                && p.Index == index
                && string.Equals(p.DeviceId, device.Profile.Id, StringComparison.OrdinalIgnoreCase));
        }

        SetActive(active);
    }

    private async Task ApplyPresetAsync(Preset preset)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var device = EnsureDevice();
            if (device is null)
            {
                ShowToast("未检测到耳机，请确认已插好", true);
                return;
            }

            if (!string.Equals(preset.DeviceId, device.Profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("该预设属于其他设备，当前耳机无法应用", true);
                return;
            }

            await Task.Run(() => PresetApplier.Apply(device, preset));

            if (preset.Kind == PresetKind.Curve)
            {
                _lastCurve = preset;
            }

            SetActive(preset);
            ShowToast($"已应用：{preset.Name}");
        }
        catch (Exception ex)
        {
            _device?.Dispose();
            _device = null;
            UpdateConnection(false, "连接中断，请重新插拔耳机");
            ShowToast($"应用失败：{ex.Message}", true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        var device = EnsureDevice();
        if (device is null)
        {
            ShowToast("未检测到耳机，请确认已插好", true);
            return;
        }

        CaptureButton.IsEnabled = false;
        try
        {
            var bands = await Task.Run(device.ReadAllBands);
            if (bands is null)
            {
                ShowToast("读取曲线失败，请在官网应用一次后再试", true);
                return;
            }

            var preGain = await Task.Run(device.ReadPreGainDb);
            var globalGain = await Task.Run(device.ReadGlobalGainDb);

            var suggested = $"曲线 {_store.Presets.Count(p =>
                p.Kind == PresetKind.Curve
                && string.Equals(p.DeviceId, device.Profile.Id, StringComparison.OrdinalIgnoreCase)) + 1}";
            var name = RenameDialog.Show(this, "抓取曲线", "给这条曲线起个名字，方便之后用热键切换：", suggested);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var preset = new Preset
            {
                Name = name.Trim(),
                Kind = PresetKind.Curve,
                DeviceId = device.Profile.Id,
                Bands = bands.ToList(),
                PreGainDb = preGain,
                GlobalGainDb = globalGain,
                Hotkey = _store.NextHotkey()
            };

            _store.Presets.Add(preset);
            _store.Save();

            _lastCurve = preset;
            RefreshItems();
            RegisterHotkeys();
            _tray?.Update(ActivePresets);
            SetActive(preset);
            ShowToast($"已保存：{preset.Name}");
        }
        catch (Exception ex)
        {
            ShowToast($"抓取失败：{ex.Message}", true);
        }
        finally
        {
            CaptureButton.IsEnabled = true;
        }
    }

    private void OpenPresetFile()
    {
        _store.Save();
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.FilePath}\"")
        {
            UseShellExecute = true
        });
    }

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        DevicePage.RefreshStatus();
        ShowPage(DeviceHost);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsHost);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(AboutHost);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        SettingsHost.Visibility = Visibility.Collapsed;
        DeviceHost.Visibility = Visibility.Collapsed;
        AboutHost.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
        FooterPanel.Visibility = Visibility.Visible;
    }

    private void ShowPage(Grid host)
    {
        if (!IsVisible)
        {
            ShowFromTray();
        }

        MainView.Visibility = Visibility.Collapsed;
        FooterPanel.Visibility = Visibility.Collapsed;
        SettingsHost.Visibility = Visibility.Collapsed;
        DeviceHost.Visibility = Visibility.Collapsed;
        AboutHost.Visibility = Visibility.Collapsed;
        host.Visibility = Visibility.Visible;
    }

    private void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement { Tag: PresetItem item })
        {
            _ = ApplyPresetAsync(item.Model);
        }
    }

    private static System.Windows.Controls.Primitives.ButtonBase? FindButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase button)
            {
                return button;
            }

            if (node is not System.Windows.Media.Visual
                && node is not System.Windows.Media.Media3D.Visual3D)
            {
                return null;
            }

            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PresetItem item })
        {
            RenamePreset(item.Model);
        }
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PresetItem item })
        {
            EditHotkey(item.Model);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PresetItem item })
        {
            DeletePreset(item.Model);
        }
    }

    private void PresetCard_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PresetItem item })
        {
            return;
        }

        e.Handled = true;

        var menu = new System.Windows.Controls.ContextMenu
        {
            Style = TryFindResource("AppContextMenuStyle") as Style
        };

        var rename = new System.Windows.Controls.MenuItem
        {
            Header = "重命名",
            Style = TryFindResource("AppMenuItemStyle") as Style
        };
        rename.Click += (_, _) => RenamePreset(item.Model);
        menu.Items.Add(rename);

        var hotkey = new System.Windows.Controls.MenuItem
        {
            Header = "设置热键…",
            Style = TryFindResource("AppMenuItemStyle") as Style
        };
        hotkey.Click += (_, _) => EditHotkey(item.Model);
        menu.Items.Add(hotkey);

        menu.Items.Add(new System.Windows.Controls.Separator
        {
            Style = TryFindResource("AppMenuSeparatorStyle") as Style
        });

        var remove = new System.Windows.Controls.MenuItem
        {
            Header = "删除",
            Style = TryFindResource("AppMenuItemStyle") as Style
        };
        remove.Click += (_, _) => DeletePreset(item.Model);
        menu.Items.Add(remove);

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void RenamePreset(Preset preset)
    {
        var name = RenameDialog.Show(this, "重命名预设", "输入新的名称：", preset.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();
        if (trimmed == preset.Name)
        {
            return;
        }

        preset.Name = trimmed;
        _store.Save();
        RefreshItems();
        SetActive(_activePreset);
        _tray?.Update(ActivePresets);
        ShowToast($"已改名为：{trimmed}");
    }

    private void EditHotkey(Preset preset)
    {
        var gesture = HotkeyDialog.Show(this, preset.Name, preset.Hotkey);
        if (gesture is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(gesture))
        {
            var duplicate = _store.Presets.FirstOrDefault(p =>
                p.Id != preset.Id &&
                string.Equals(p.Hotkey, gesture, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                ShowToast($"这个热键已被「{duplicate.Name}」占用", true);
                return;
            }
        }

        preset.Hotkey = gesture;
        _store.Save();
        RefreshItems();
        SetActive(_activePreset);
        RegisterHotkeys();
        _tray?.Update(ActivePresets);
        ShowToast(string.IsNullOrWhiteSpace(gesture) ? "已清除热键" : $"热键已设为 {gesture}");
    }

    private void DeletePreset(Preset preset)
    {
        if (!ConfirmDialog.Show(this, "删除预设",
                $"确定删除「{preset.Name}」吗？\n\n只会从本地列表移除，不会改动耳机内的设置。"))
        {
            return;
        }

        _store.Presets.Remove(preset);
        _store.Save();

        if (_activePreset == preset)
        {
            _activePreset = null;
        }
        if (_lastCurve == preset)
        {
            _lastCurve = null;
        }

        RefreshItems();
        RegisterHotkeys();
        _tray?.Update(ActivePresets);
        ShowToast($"已删除：{preset.Name}");
    }

    private IEnumerable<Preset> ActivePresets => _store.Presets.Where(preset =>
        string.Equals(preset.DeviceId, _activeDeviceId, StringComparison.OrdinalIgnoreCase));

    private void ApplyProfile(DeviceProfile profile)
    {
        var changed = !string.Equals(_activeDeviceId, profile.Id, StringComparison.OrdinalIgnoreCase);
        _activeDeviceId = profile.Id;
        if (changed)
        {
            _lastCurve = null;
        }

        _store.EnsureDefaultsForDevice(profile);
        CaptureButton.IsEnabled = profile.Capabilities.Curve;
        RefreshItems();
        if (changed)
        {
            SetActive(null);
        }
        RegisterHotkeys();
        _tray?.Update(ActivePresets);
    }

    private void RefreshItems()
    {
        _items.Clear();
        foreach (var preset in ActivePresets)
        {
            _items.Add(new PresetItem(preset));
        }
    }

    private void SetActive(Preset? preset)
    {
        _activePreset = preset;

        foreach (var item in _items)
        {
            item.IsActive = preset is not null && item.Model.Id == preset.Id;
        }

        if (preset is null)
        {
            ActiveText.Text = string.Empty;
        }
        else
        {
            ActiveText.Text = $"当前：{preset.Name}";
        }
    }

    private void UpdateConnection(bool? connected, string message)
    {
        StatusText.Text = message;
        StatusDot.Fill = connected switch
        {
            true => (System.Windows.Media.Brush)FindResource("AppSuccessBrush"),
            false => (System.Windows.Media.Brush)FindResource("AppDangerBrush"),
            _ => (System.Windows.Media.Brush)FindResource("AppTextTertiaryBrush")
        };
    }

    private void ShowToast(string message, bool isError = false)
    {
        ToastText.Text = message;
        ToastText.Foreground = (System.Windows.Media.Brush)FindResource(
            isError ? "AppDangerBrush" : "AppTextPrimaryBrush");
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        DisposeInfrastructure();
        Application.Current.Shutdown();
    }

    private void DisposeInfrastructure()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        _tray?.Dispose();
        _tray = null;
        _device?.Dispose();
        _device = null;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (ThemeService.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _exiting = true;
        DisposeInfrastructure();
        Application.Current.Shutdown();
    }
}
