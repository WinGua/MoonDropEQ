using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MoonDropEQ.Models;

public sealed class PresetItem : INotifyPropertyChanged
{
    private bool _isActive;

    public PresetItem(Preset model)
    {
        Model = model;
    }

    public Preset Model { get; }

    public string Name => Model.Name;

    public string KindText => Model.Kind == PresetKind.BuiltIn ? "内置预设" : "自定义曲线";

    public string HotkeyText => string.IsNullOrWhiteSpace(Model.Hotkey) ? "未绑定" : Model.Hotkey;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
