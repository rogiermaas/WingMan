using System.Runtime.InteropServices;

namespace EscortPlane2024;

internal static class WindowPinning
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    public static bool IsTopmost(nint window) => (GetWindowLong(window, -20) & 8) != 0;
    public static bool Apply(Form form, bool enabled)
    {
        if (!form.IsHandleCreated) return true;
        // Form.TopMost's setter calls SetWindowPos without SWP_NOACTIVATE, even
        // when its cached value is unchanged. Use only the native call here;
        // HandleCreated/Shown reapply the user's setting when a handle is created.
        return SetWindowPos(form.Handle, enabled ? -1 : -2, 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x200);
    }
}
