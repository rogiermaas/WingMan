namespace EscortPlane2024;

// Live label resizing must not scroll the settings back to its focused input.
internal sealed class StableScrollPanel : FlowLayoutPanel
{
    public StableScrollPanel() { DoubleBuffered = true; }
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
    public IDisposable PreserveScroll() => new ScrollRestore(this);
    private sealed class ScrollRestore : IDisposable
    {
        private readonly StableScrollPanel panel;
        private readonly Point position;
        public ScrollRestore(StableScrollPanel panel)
        {
            this.panel = panel; position = panel.AutoScrollPosition; panel.SuspendLayout();
        }
        public void Dispose()
        {
            panel.ResumeLayout(true);
            panel.AutoScrollPosition = new(-position.X, -position.Y);
        }
    }
}
