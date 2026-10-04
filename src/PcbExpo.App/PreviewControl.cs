using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PcbExpo.Core;

namespace PcbExpo.App;

public sealed class PreviewControl : Control
{
    private Bitmap? _bitmap;
    private PrinterInfo? _printer;
    private ProjectModel? _project;
    private RectMm _blankOnLcd;
    private IReadOnlyList<RectMm> _boards = [];
    private readonly BlankLayoutService _layout = new();
    private double _zoom = 1;
    private Vector _pan;
    private Point? _lastPointer;
    private Point? _dragStart;
    private PointMm _positionStart;
    private bool _panning;
    private bool _draggingBoard;

    public Action<PointMm, bool>? PositionChanged { get; set; }

    public PreviewControl() => ClipToBounds = true;

    public void SetPreview(Bitmap bitmap, PrinterInfo printer, ProjectModel project,
        RectMm blankOnLcd, IReadOnlyList<RectMm> boards)
    {
        _bitmap?.Dispose();
        _bitmap = bitmap;
        _printer = printer;
        _project = project;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _blankOnLcd = blankOnLcd;
        _boards = boards;
        InvalidateVisual();
    }

    public void ClearPreview()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _printer = null;
        _project = null;
        _boards = [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var viewport = new Rect(Bounds.Size);
        using var clip = context.PushClip(viewport);
        context.FillRectangle(Brushes.WhiteSmoke, viewport);
        if (_bitmap is null || _printer is null || _project is null) return;
        var area = ViewRect();
        context.DrawImage(_bitmap, area);
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1), area);
        // В центровке точки, кольца и рамка уже входят в растр с физической толщиной.
        if (_project.Mode == ExposureMode.Registration) return;
        DrawMmRect(context, _blankOnLcd, new Pen(Brushes.DeepSkyBlue, 2));
        foreach (var board in _boards)
        {
            var physical = _draggingBoard && _boards.Count == 1
                ? new RectMm(_project.IsBottom
                    ? _project.Blank.WidthMm - _project.PcbPositionMm.X - board.Width
                    : _project.PcbPositionMm.X, _project.PcbPositionMm.Y, board.Width, board.Height)
                : board;
            DrawMmRect(context, new RectMm(_blankOnLcd.X + physical.X,
                _blankOnLcd.Y + physical.Y, physical.Width, physical.Height), new Pen(Brushes.LimeGreen, 1));
        }
        try
        {
            foreach (var hole in _layout.MechanicalHoles(_project.Blank))
            {
                var center = ToScreen(new PointMm(_blankOnLcd.X + hole.X,
                    _blankOnLcd.Y + hole.Y));
                var radius = _project.Blank.RegistrationHoleDiameterMm * MmScale() / 2;
                context.DrawEllipse(null, new Pen(Brushes.OrangeRed, 1), center, radius, radius);
            }
        }
        catch (InvalidOperationException) { }
    }

    private void DrawMmRect(DrawingContext context, RectMm rect, Pen pen)
    {
        var a = ToScreen(new PointMm(rect.X, rect.Top));
        context.DrawRectangle(null, pen, new Rect(a.X, a.Y,
            rect.Width * MmScale(), rect.Height * MmScale()));
    }

    private Rect ViewRect()
    {
        if (_printer is null) return Bounds;
        var scale = MmScale();
        return new Rect((Bounds.Width - _printer.DisplayWidthMm * scale) / 2 + _pan.X,
            (Bounds.Height - _printer.DisplayHeightMm * scale) / 2 + _pan.Y,
            _printer.DisplayWidthMm * scale, _printer.DisplayHeightMm * scale);
    }

    private double MmScale() => _printer is null ? 1 :
        Math.Min(Bounds.Width / _printer.DisplayWidthMm, Bounds.Height / _printer.DisplayHeightMm) * _zoom;

    private Point ToScreen(PointMm lcd)
    {
        var area = ViewRect();
        return new Point(area.X + lcd.X * MmScale(), area.Y + (_printer!.DisplayHeightMm - lcd.Y) * MmScale());
    }

    private PointMm ToLcd(Point screen)
    {
        var area = ViewRect();
        return new PointMm((screen.X - area.X) / MmScale(),
            _printer!.DisplayHeightMm - (screen.Y - area.Y) / MmScale());
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.5, 12);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_printer is null || _project is null || _bitmap is null) return;
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            _panning = true;
            _lastPointer = p;
        }
        else if (props.IsLeftButtonPressed && _project.Panelization.Mode == PcbExpo.Core.PlacementMode.Single &&
                 _boards.Count == 1 && _project.Mode is not (ExposureMode.Registration or ExposureMode.Calibration or ExposureMode.ExposureCalibration))
        {
            var lcd = ToLcd(p);
            var local = new PointMm(lcd.X - _blankOnLcd.X, lcd.Y - _blankOnLcd.Y);
            var board = _boards[0];
            if (local.X >= board.X && local.X <= board.Right && local.Y >= board.Y && local.Y <= board.Top)
            {
                _draggingBoard = true;
                _dragStart = p;
                _positionStart = _project.PcbPositionMm;
            }
        }
        if (_panning || _draggingBoard) e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_panning && _lastPointer is { } previous)
        {
            _pan += p - previous;
            _lastPointer = p;
            InvalidateVisual();
        }
        else if (_draggingBoard && _dragStart is { } start && _project is not null)
        {
            var dx = (p.X - start.X) / MmScale() * (_project.IsBottom ? -1 : 1);
            var dy = -(p.Y - start.Y) / MmScale();
            _project.PcbPositionMm = new PointMm(_positionStart.X + dx, _positionStart.Y + dy);
            PositionChanged?.Invoke(_project.PcbPositionMm, false);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingBoard && _project is not null) PositionChanged?.Invoke(_project.PcbPositionMm, true);
        _draggingBoard = false;
        _panning = false;
        _lastPointer = null;
        _dragStart = null;
        e.Pointer.Capture(null);
    }
}
