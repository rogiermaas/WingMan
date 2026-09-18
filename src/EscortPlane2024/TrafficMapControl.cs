using System.Drawing.Drawing2D;

namespace EscortPlane2024;

internal sealed class TrafficMapControl : Control
{
    private readonly ToolTip help = new() { AutoPopDelay = 12000 };
    private readonly List<(RectangleF Bounds, TrafficContact Contact)> hits = [];
    private IReadOnlyList<TrafficContact> traffic = [];
    private OwnTelemetry? own;
    private double range = 20;
    private ContactId? selected;
    private ContactId? hovering;
    public event Action<TrafficContact>? ContactClicked;
    public event Action<int>? ZoomRequested;
    public TrafficMapControl()
    {
        DoubleBuffered = true; Dock = DockStyle.Fill; TabStop = true;
        BackColor = Color.FromArgb(17, 28, 40); ForeColor = Color.WhiteSmoke;
        AccessibleName = "Nearby traffic map";
        AccessibleDescription = "North-up range rings centered on your aircraft. Click a contact ID to follow. All IDs also appear in the keyboard-accessible list.";
    }
    public void UpdateTraffic(OwnTelemetry? aircraft, IReadOnlyList<TrafficContact> contacts, double radius, ContactId? chosen)
    { own = aircraft; traffic = contacts; range = radius; selected = chosen; Invalidate(); }
    internal static PointF Project(Position origin, Position position, PointF center, float pixels, double radius)
    {
        var (north, east) = FormationGeometry.Displacement(origin, position);
        return new(center.X + (float)(east / radius * pixels), center.Y - (float)(north / radius * pixels));
    }
    internal TrafficContact? HitTest(Point point) => hits.AsEnumerable().Reverse().FirstOrDefault(h => h.Bounds.Contains(point)).Contact;
    internal void SelectAt(Point point)
    {
        var contact = HitTest(point);
        if (contact != null) ContactClicked?.Invoke(contact);
    }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); Focus(); if (e.Button == MouseButtons.Left) SelectAt(e.Location); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ZoomRequested?.Invoke(e.Delta > 0 ? -1 : 1); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var contact = HitTest(e.Location); Cursor = contact == null ? Cursors.Default : Cursors.Hand;
        if (contact?.Id == hovering) return;
        hovering = contact?.Id;
        help.SetToolTip(this, contact == null ? "Mouse wheel changes range. North is up; your aircraft is at the center." :
            $"{contact.Id} · {(contact.Name.Length > 0 ? contact.Name : "No name supplied")}\n{contact.Model}\n{contact.Detail}\n" +
            (contact.Fresh(DateTimeOffset.UtcNow) ? "Click to follow this ID once its flight motion is ready." : "Position is stale — waiting for an update."));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); hits.Clear();
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(58, 94, 113));
        using var dim = new SolidBrush(Color.FromArgb(162, 190, 205));
        using var white = new SolidBrush(ForeColor);
        var center = new PointF(Width / 2f, (Height - 25) / 2f);
        var pixels = Math.Max(10, Math.Min(Width - 70, Height - 65) / 2f);
        for (int i = 1; i <= 4; i++)
        {
            var r = pixels * i / 4;
            g.DrawEllipse(grid, center.X - r, center.Y - r, r * 2, r * 2);
            if (i % 2 == 0) g.DrawString($"{range * i / 4:0.##}", Font, dim, center.X + 3, center.Y - r);
        }
        g.DrawLine(grid, center.X - pixels, center.Y, center.X + pixels, center.Y);
        g.DrawLine(grid, center.X, center.Y - pixels, center.X, center.Y + pixels);
        g.DrawString("N", Font, white, center.X - 7, Math.Max(0, center.Y - pixels - 20));
        g.DrawString($"{range:0.##} NM range", Font, dim, 7, 4);
        g.DrawString("T = map traffic   S = aircraft object   Gray = stale", Font, dim, 7, Height - 23);
        if (own == null)
        {
            g.DrawString("Waiting for your aircraft's position", Font, white, 7, 27);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var labels = new List<RectangleF> { new(center.X - 21, center.Y - 12, 45, 40) };
        var points = traffic.Where(c => c.Position != null && own.Position.DistanceNm(c.Position) <= range)
            .OrderByDescending(c => c.Id == selected)
            .Select(c => (Contact: c, Point: Project(own.Position, c.Position!, center, pixels, range))).ToArray();
        var markers = points.Select(p => new RectangleF(p.Point.X - 7, p.Point.Y - 7, 14, 14)).ToArray();
        int plotted = 0;
        int unlabelled = 0;
        foreach (var (contact, point) in points)
        {
            var color = contact.Id == selected ? Color.Gold : !contact.Fresh(now) ? Color.Gray : contact.Id.SimObject ? Color.LightSkyBlue : Color.Aquamarine;
            using var brush = new SolidBrush(color); using var pen = new Pen(color, 1.5f);
            g.DrawPolygon(pen, new PointF[] { new(point.X, point.Y - 5), new(point.X + 5, point.Y), new(point.X, point.Y + 5), new(point.X - 5, point.Y) });
            var text = string.IsNullOrWhiteSpace(contact.Name) ? contact.Id.ToString() : contact.Name;
            var size = g.MeasureString(text, Font);
            var candidates = new List<RectangleF>();
            for (float y = 25; y + size.Height < Height - 26; y += size.Height + 2)
            {
                candidates.Add(new(Math.Clamp(point.X + 9, 3, Math.Max(3, Width - size.Width - 3)), y, size.Width, size.Height));
                for (float x = 4; x + size.Width < Width - 3; x += size.Width + 8) candidates.Add(new(x, y, size.Width, size.Height));
            }
            var label = candidates.Where(c => !labels.Any(r => r.IntersectsWith(c)) && !markers.Any(r => r.IntersectsWith(c)))
                .OrderBy(c => Math.Pow(c.X + c.Width / 2 - point.X, 2) + Math.Pow(c.Y + c.Height / 2 - point.Y, 2)).FirstOrDefault();
            hits.Add((new(point.X - 8, point.Y - 8, 16, 16), contact)); plotted++;
            if (label.IsEmpty) { unlabelled++; continue; }
            var end = new PointF(Math.Clamp(point.X, label.Left, label.Right), Math.Clamp(point.Y, label.Top, label.Bottom));
            using var leader = new Pen(Color.FromArgb(100, color)); g.DrawLine(leader, point, end);
            using var background = new SolidBrush(BackColor);
            g.FillRectangle(background, label); g.DrawString(text, Font, brush, label.Location);
            labels.Add(label); hits.Add((label, contact));
        }
        var angle = own.TrueTrack * Math.PI / 180;
        PointF Rotate(float x, float y) => new(center.X + x * (float)Math.Cos(angle) - y * (float)Math.Sin(angle), center.Y + x * (float)Math.Sin(angle) + y * (float)Math.Cos(angle));
        g.FillPolygon(white, [Rotate(0, -9), Rotate(6, 7), Rotate(0, 3), Rotate(-6, 7)]);
        g.DrawString("YOU", Font, white, center.X - 17, center.Y + 10);
        if (plotted == 0) g.DrawString("No contacts with usable positions in range", Font, dim, 7, Height - 45);
        else if (unlabelled > 0) g.DrawString($"{unlabelled} crowded labels: zoom in or use All IDs", Font, dim, 7, 24);
    }
    protected override void Dispose(bool disposing) { if (disposing) help.Dispose(); base.Dispose(disposing); }
}
