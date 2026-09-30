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
            foreach (var path in paths) original.AddRange(new ExcellonDrillService().Read(path, allowEmpty: true));
        }
        if (original.Count == 0) throw new InvalidOperationException("Нет геометрии для CNC.");
        return Place(project, bounds, original);
    }

    public IReadOnlyList<DxfContour> Build(ProjectModel project, GerberPackage package, CncExportSettings options)
    {
        if (package.BoardBoundsMm is not { Width: > 0, Height: > 0 } bounds)
            throw new InvalidOperationException("Выберите контур платы для определения раскладки.");
        var original = new List<DxfContour>();
        if (options.BoardOutlines || options.BoardCutouts)
        {
            if (package.OutlineFallback || !project.LayerPaths.TryGetValue(GerberLayerKind.BoardOutline, out var path))
                throw new InvalidOperationException("Для CNC нужен выбранный Gerber контура платы.");
            var outline = new GerberOutlineService().Read(path);
            if (outline.Any(c => c is not DxfPolyline { Closed: true }))
                throw new InvalidOperationException("Для CNC все контуры платы и вырезов должны быть замкнуты.");
            var polygons = outline.Select(DxfGeometry.Points).ToArray();
            for (var i = 0; i < outline.Count; i++)
            {
                // Nesting, rather than winding direction, distinguishes outer edges and internal cutouts.
                var depth = polygons.Where((p, j) => j != i && Contains(p, polygons[i][0])).Count();
                var cutout = depth % 2 != 0;
                if (cutout ? options.BoardCutouts : options.BoardOutlines)
                    original.Add(((DxfPolyline)outline[i]) with { Layer = cutout ? "BOARD_CUTOUT" : "BOARD_OUTLINE" });
            }
        }
        foreach (var drill in SelectedDrills(project, package))
        {
            var kind = options.KindFor(drill);
            if (!options.Includes(kind)) continue;
            var layer = kind switch
            {
                CncDrillKind.Via => "DRILL_VIA",
                CncDrillKind.Component => "DRILL_COMPONENT",
                _ => "DRILL_BOARD"
            };
            original.AddRange(new ExcellonDrillService().Read(drill.Path, allowEmpty: true).Select(c => c switch
            {
                DxfCircle circle => (DxfContour)(circle with { Layer = layer }),
                DxfPolyline slot => slot with { Layer = layer + "_SLOTS" },
                _ => throw new InvalidOperationException("Неподдерживаемая сверловка.")
            }));
        }
        // Validate and apply the same panelization even when only blank geometry is selected.
        var result = Place(project, bounds, original).ToList();
        if (options.BlankOutline || options.RegistrationHoles)
            result.AddRange(new ContourService().Blank(project.Blank).Where(c =>
                c.Layer == "BLANK" ? options.BlankOutline : options.RegistrationHoles));
        if (result.Count == 0) throw new InvalidOperationException("Выберите хотя бы один элемент экспорта CNC.");
        return result;
    }

    public static IReadOnlyList<GerberLayer> SelectedDrills(ProjectModel project, GerberPackage package) =>
        package.Layers.Where(l => l.Kind == GerberLayerKind.Drill &&
            (!project.LayerPaths.TryGetValue(GerberLayerKind.Drill, out var path) ||
                string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase))).ToArray();

    private static bool Contains(IReadOnlyList<PointMm> polygon, PointMm point)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<DxfContour> Place(ProjectModel project, RectangleF bounds, IReadOnlyList<DxfContour> original)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height))
            throw new InvalidOperationException("Некорректные габариты платы.");
        ValidateBounds(original, bounds);
        var boards = new PanelizationService(new BlankLayoutService()).LayoutPhysical(project, bounds.Width, bounds.Height);
        var boardMode = project.Mode is ExposureMode.TopCopper or ExposureMode.BottomCopper or
            ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask or ExposureMode.TopStencil or ExposureMode.BottomStencil;
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
            if (transform.MirrorX ^ project.IsBottom) x = bounds.Width - x;
            if (transform.MirrorY) y = bounds.Height - y;
            return new PointMm(board.X + x, board.Y + y);
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
