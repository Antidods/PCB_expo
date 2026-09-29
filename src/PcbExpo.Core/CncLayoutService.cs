using System.Drawing;

namespace PcbExpo.Core;

/// <summary>Геометрия CNC и её preview используют одну раскладку в координатах заготовки.</summary>
public sealed class CncLayoutService
{
    public IReadOnlyList<DxfContour> Build(ProjectModel project, GerberPackage package,
        bool includeOutline = true, bool includeDrills = true)
    {
        if (package.BoardBoundsMm is not { } bounds)
            throw new InvalidOperationException("Выберите контур платы для определения её размеров и начала координат.");
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("Некорректные габариты платы.");
        var original = new List<DxfContour>();
        if (includeOutline)
        {
            if (package.OutlineFallback || !project.LayerPaths.TryGetValue(GerberLayerKind.BoardOutline, out var path))
                throw new InvalidOperationException("Для CNC нужен выбранный Gerber контура платы. Габариты по меди не заменяют контур фрезеровки.");
            var outline = new GerberOutlineService().Read(path);
            if (outline.Any(c => c is not DxfPolyline { Closed: true }))
                throw new InvalidOperationException("Для CNC все контуры платы и вырезов должны быть замкнуты. Проверьте Gerber контура.");
            original.AddRange(outline);
        }
        if (includeDrills)
        {
            var paths = project.LayerPaths.TryGetValue(GerberLayerKind.Drill, out var selected)
                ? new[] { selected } : package.Layers.Where(l => l.Kind == GerberLayerKind.Drill).Select(l => l.Path).ToArray();
            if (paths.Length == 0) throw new InvalidOperationException("В наборе нет файлов сверловки .drl/.xln.");
            foreach (var path in paths) original.AddRange(new ExcellonDrillService().Read(path));
        }
        if (original.Count == 0) throw new InvalidOperationException("Нет геометрии для CNC.");
        ValidateBounds(original, bounds);
        var boards = new PanelizationService(new BlankLayoutService()).Layout(bounds.Width, bounds.Height,
            project.Blank, project.PcbPositionMm, project.Panelization);
        var boardMode = project.Mode is ExposureMode.TopCopper or ExposureMode.BottomCopper or
            ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask;
        var transform = boardMode && project.Transformations.TryGetValue(project.Mode, out var settings)
            ? settings : new TransformationSettings();
        var reflected = transform.MirrorX ^ transform.MirrorY ^ project.IsBottom;
        return boards.SelectMany(board => original.Select(contour => contour switch
        {
            DxfCircle circle => (DxfContour)(circle with { Center = Map(circle.Center, board) }),
            DxfPolyline line => line with
            {
                Points = line.Points.Select(p => Map(p, board)).ToArray(),
                Bulges = line.Bulges?.Select(b => reflected ? -b : b).ToArray()
            },
            _ => throw new InvalidOperationException("Неподдерживаемая геометрия CNC.")
        })).ToArray();

        PointMm Map(PointMm point, RectMm board)
        {
            var x = point.X - bounds.X; var y = point.Y - bounds.Y;
            if (transform.MirrorX) x = bounds.Width - x;
            if (transform.MirrorY) y = bounds.Height - y;
            var placed = new PointMm(board.X + x, board.Y + y);
            return project.IsBottom ? new CoordinateTransformService().FlipBlankAroundVerticalAxis(placed, project.Blank) : placed;
        }
    }

    private static void ValidateBounds(IEnumerable<DxfContour> contours, RectangleF bounds)
    {
        const double tolerance = 0.01;
        foreach (var contour in contours)
        {
            var points = DxfGeometry.Points(contour);
            if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
                p.X < bounds.Left - tolerance || p.X > bounds.Right + tolerance ||
                p.Y < bounds.Top - tolerance || p.Y > bounds.Bottom + tolerance))
                throw new InvalidOperationException("Контур или сверловка выходит за габариты платы. Проверьте единицы, формат координат и общий ноль Gerber/Excellon.");
        }
    }
}
