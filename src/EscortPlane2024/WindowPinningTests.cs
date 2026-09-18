using System.Runtime.InteropServices;

namespace EscortPlane2024;

internal static class WindowPinningTests
{
    private sealed class CachedTopmostForm : Form
    {
        // TopMost=true during handle creation may activate even a hidden form in
        // the bundled runtime. Prevent activation in this cache-setup fixture.
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; }
        }
    }
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int cx, int cy, uint flags);
    public static void Run(Action<bool, string> check)
    {
        // Hidden test window: no focus change and no interaction with the running app.
        using var form = new CachedTopmostForm { TopMost = true }; _ = form.Handle;
        check(WindowPinning.Apply(form, true) && WindowPinning.IsTopmost(form.Handle), "Keep on top sets the native topmost state");
        SetWindowPos(form.Handle, -2, 0, 0, 0, 0, 0x01 | 0x02 | 0x10 | 0x200);
        check(form.TopMost && !WindowPinning.IsTopmost(form.Handle)
            && WindowPinning.Apply(form, true) && WindowPinning.IsTopmost(form.Handle),
            "Keep on top repairs native state when WinForms still caches true");
        check(WindowPinning.Apply(form, false) && !WindowPinning.IsTopmost(form.Handle), "Turning keep on top off removes the native topmost state");
        // Use an ordinary window (no WS_EX_NOACTIVATE) for the real activation
        // regression. No TopMost setter is involved in this fixture's setup.
        using var ordinary = new Form(); _ = ordinary.Handle;
        var activations = 0;
        ordinary.Activated += (_, _) => activations++;
        WindowPinning.Apply(ordinary, true); WindowPinning.Apply(ordinary, true); WindowPinning.Apply(ordinary, false);
        check(activations == 0 && WindowPinning.GetForegroundWindow() != ordinary.Handle,
            "Topmost maintenance does not activate its window or steal foreground focus");
    }
}
