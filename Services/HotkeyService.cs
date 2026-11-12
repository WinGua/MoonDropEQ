using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MoonDropEQ.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly IntPtr _handle;
    private readonly HwndSource? _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xA000;

    public HotkeyService(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
    }

    public bool TryRegister(string gesture, Action action, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "未设置热键";
            return false;
        }

        if (!TryParse(gesture, out var modifiers, out var virtualKey))
        {
            error = "热键格式无法识别";
            return false;
        }

        var id = _nextId++;
        if (!RegisterHotKey(_handle, id, modifiers | ModNoRepeat, virtualKey))
        {
            error = "热键已被其他程序占用";
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_handle, id);
        }
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == parts.Length - 1)
            {
                if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                {
                    virtualKey = char.ToUpperInvariant(part[0]);
                }
                else if (part.Length is 2 or 3 && (part[0] is 'F' or 'f') && int.TryParse(part[1..], out var fn) && fn is >= 1 and <= 24)
                {
                    virtualKey = (uint)(0x70 + fn - 1);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        break;
                    case "alt":
                        modifiers |= ModAlt;
                        break;
                    case "shift":
                        modifiers |= ModShift;
                        break;
                    case "win":
                        modifiers |= ModWin;
                        break;
                    default:
                        return false;
                }
            }
        }

        return virtualKey != 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
