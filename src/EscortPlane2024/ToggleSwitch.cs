using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace EscortPlane2024;

// Own the entire window surface: a native checkbox can repaint part of its glyph
// during focus/hover transitions even when its normal paint method is overridden.
internal sealed class ToggleSwitch : Control
{
    private bool isChecked, spacePressed;
    [DefaultValue(true)]
    public bool AutoCheck { get; set; } = true;
    [DefaultValue(false)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value; Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? CheckedChanged;
    public ToggleSwitch()
    {
        AutoSize = false; Size = new(52, 28); Margin = new(6, 0, 12, 2);
        Cursor = Cursors.Hand; TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
    }
    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        if (AutoCheck) Checked = !Checked;
        base.OnClick(e);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { if (e.Button == MouseButtons.Left) Focus(); base.OnMouseDown(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyData == Keys.Space) { spacePressed = true; e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            var activate = spacePressed && e.Modifiers == Keys.None; spacePressed = false;
            e.Handled = e.SuppressKeyPress = true;
            if (activate && Enabled && Focused) OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { spacePressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { spacePressed = false; base.OnEnabledChanged(e); Invalidate(); }
    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessibility(this);
    private sealed class SwitchAccessibility(ToggleSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.CheckButton;
        public override AccessibleStates State => base.State | AccessibleStates.Focusable | (owner.Checked ? AccessibleStates.Checked : 0);
        public override string DefaultAction => owner.Checked ? "Turn off" : "Turn on";
        public override void DoDefaultAction() { if (owner.Enabled) owner.OnClick(EventArgs.Empty); }
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Color.White);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(2, 2, Width - 4, Height - 4);
        float diameter = bounds.Height;
        using var shape = new GraphicsPath();
        shape.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
        shape.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180); shape.CloseFigure();
        var color = Checked ? Color.FromArgb(45, 178, 97) : Color.FromArgb(217, 70, 77);
        if (!Enabled) color = Color.FromArgb((color.R + 220) / 2, (color.G + 220) / 2, (color.B + 220) / 2);
        using var fill = new SolidBrush(color); e.Graphics.FillPath(fill, shape);
        float knob = diameter - 4, x = Checked ? bounds.Right - knob - 2 : bounds.Left + 2;
        e.Graphics.FillEllipse(Brushes.White, x, bounds.Top + 2, knob, knob);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.FromArgb(38, 92, 143), 1.5f);
            e.Graphics.DrawPath(focus, shape);
        }
    }
}
