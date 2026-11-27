using System.Runtime.InteropServices;

namespace Desktop3D.Controller;

/// <summary>Message-only window that owns the global hotkeys.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public const uint ModAlt = 0x1, ModCtrl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;
    readonly List<int> ids = new();

    public event Action<int>? Pressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
    }

    public bool Register(int id, uint mods, Keys key)
    {
        bool ok = RegisterHotKey(Handle, id, mods | ModNoRepeat, (uint)key);
        if (ok) ids.Add(id);
        return ok;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey) Pressed?.Invoke((int)m.WParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in ids) UnregisterHotKey(Handle, id);
        ids.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
