namespace EscortPlane2024;

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel() { DoubleBuffered = true; }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel() { DoubleBuffered = true; }
}

internal sealed class BufferedTabControl : TabControl
{
    public BufferedTabControl() { DoubleBuffered = true; }
}
