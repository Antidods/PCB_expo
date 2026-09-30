using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PcbExpo.Core;

namespace PcbExpo.App;

internal sealed class DxfPreviewControl : Control
{
    private IReadOnlyList<(DxfContour Contour, IReadOnlyList<PointMm> Points)> _shapes = [];
    private RectMm _extent = new(0, 0, 1, 1);
    private RectMm? _blank;
    private IReadOnlyList<RectMm> _boards = [];
    private double _zoom = 1;
    private Vector _pan;
    private Point? _pointer;

    public DxfPreviewControl() => ClipToBounds = true;

    public void SetContours(IReadOnlyList<DxfContour> contours, RectMm? blank = null, IReadOnlyList<RectMm>? boards = null)
    {
        _shapes = contours.Select(c => (c, DxfGeometry.Points(c))).ToArray();
        _blank = blank;
        _boards = boards ?? [];
        var points = _shapes.SelectMany(s => s.Points).ToList();
        if (blank is { } bounds) { points.Add(new(bounds.X, bounds.Y)); points.Add(new(bounds.Right, bounds.Top)); }
        if (points.Count > 0)
        {
            var x = points.Min(p => p.X); var y = points.Min(p => p.Y);
            _extent = new RectMm(x, y, Math.Max(1, points.Max(p => p.X) - x), Math.Max(1, points.Max(p => p.Y) - y));
        }
        _zoom = 1; _pan = default; InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewport = new Rect(Bounds.Size);
        using var clip = context.PushClip(viewport);
        context.FillRectangle(Brushes.WhiteSmoke, viewport);
        var scale = Math.Max(0.001, Math.Min((Bounds.Width - 32) / _extent.Width, (Bounds.Height - 32) / _extent.Height)) * _zoom;
        Point Screen(PointMm p) => new((Bounds.Width - _extent.Width * scale) / 2 + (p.X - _extent.X) * scale + _pan.X,
            (Bounds.Height - _extent.Height * scale) / 2 + (_extent.Top - p.Y) * scale + _pan.Y);
        if (_blank is { } blank)
            context.DrawRectangle(null, new Pen(Brushes.Gray, 1), new Rect(Screen(new(blank.X, blank.Top)),
                new Size(blank.Width * scale, blank.Height * scale)));
        foreach (var board in _boards)
            context.DrawRectangle(null, new Pen(Brushes.DimGray, 1), new Rect(Screen(new(board.X, board.Top)),
                new Size(board.Width * scale, board.Height * scale)));
        foreach (var (contour, points) in _shapes)
        {
            var color = contour.Layer is "BLANK" or "MECHANICAL_HOLES" ? Brushes.SlateGray :
                contour.Layer.StartsWith("DRILL", StringComparison.Ordinal) ? Brushes.DodgerBlue : Brushes.ForestGreen;
            var pen = new Pen(color, 1.4);
            if (contour is DxfCircle circle)
            {
                var center = Screen(circle.Center);
                context.DrawEllipse(null, pen, center, circle.RadiusMm * scale, circle.RadiusMm * scale);
                context.DrawLine(pen, new Point(center.X - 2, center.Y), new Point(center.X + 2, center.Y));
                context.DrawLine(pen, new Point(center.X, center.Y - 2), new Point(center.X, center.Y + 2));
                continue;
            }
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(Screen(points[0]), false);
                foreach (var point in points.Skip(1)) drawing.LineTo(Screen(point));
                drawing.EndFigure(contour is DxfPolyline { Closed: true });
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 0.5, 30);
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        _pointer = e.GetPosition(this); e.Pointer.Capture(this);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_pointer is not { } previous) return;
        var next = e.GetPosition(this); _pan += next - previous; _pointer = next; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _pointer = null; e.Pointer.Capture(null);
    }
}
