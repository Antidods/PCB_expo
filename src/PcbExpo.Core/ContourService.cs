using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace PcbExpo.Core;

public sealed class ContourService
{
    public IReadOnlyList<DxfContour> Blank(BlankProfile blank)
    {
        if (!double.IsFinite(blank.WidthMm) || !double.IsFinite(blank.HeightMm) || blank.WidthMm <= 0 || blank.HeightMm <= 0)
            throw new InvalidOperationException("Размер заготовки должен быть положительным.");
        return [Rectangle("BLANK", new RectMm(0, 0, blank.WidthMm, blank.HeightMm)),
            ..new BlankLayoutService().MechanicalHoles(blank).Select(p =>
                new DxfCircle("MECHANICAL_HOLES", p, blank.RegistrationHoleDiameterMm / 2))];
    }

    public IReadOnlyList<DxfContour> Registration(BlankProfile blank)
    {
        if (!double.IsFinite(blank.ServiceLineThicknessMm) || blank.ServiceLineThicknessMm <= 0)
            throw new InvalidOperationException("Толщина служебных линий должна быть положительной.");
        return new BlankLayoutService().AlignmentPoints(blank)
            .Select(p => (DxfContour)new DxfCircle("REGISTRATION", p, blank.ServiceLineThicknessMm / 2)).ToArray();
    }

    public IReadOnlyList<DxfContour> Calibration(BlankProfile blank)
    {
        var pattern = CalibrationPattern.Create(blank);
        return [new DxfPolyline("CALIBRATION", [pattern.LineStart, pattern.LineEnd], false),
            Rectangle("CALIBRATION", pattern.Square)];
    }

    public IReadOnlyList<DxfContour> Gerber(string path)
    {
        var bounds = GerberImportService.MeasureBounds(path) ??
            throw new InvalidOperationException("В выбранном Gerber нет рисунка.");
        // Preserve aperture widths, isolated flashes, and pixels at the edge with a padded canvas.
        const double density = 100; // 0.01 mm per pixel, independent of a printer template
        for (double padding = 5; ; padding *= 2)
        {
            var width = bounds.Width + padding * 2;
            var height = bounds.Height + padding * 2;
            if (width * height * density * density > 250_000_000)
                throw new InvalidOperationException("Слой слишком велик для экспорта с шагом 0,01 мм.");
            var canvas = new RectangleF(bounds.X - (float)padding, bounds.Y - (float)padding, (float)width, (float)height);
            var raster = new RasterGeometry((int)Math.Ceiling(width * density), (int)Math.Ceiling(height * density),
                Math.Ceiling(width * density) / density, Math.Ceiling(height * density) / density);
            using var image = new GerberRenderService().RenderBoard(path, canvas, raster, false);
            // Gerber bounds are floats and the renderer rounds its dimensions; match the actual canvas.
            var renderedRaster = new RasterGeometry(image.Width, image.Height, image.Width / density, image.Height / density);
            var drawn = CvInvoke.BoundingRectangle(image);
            // Grow the canvas until large apertures fit; never export a clipped contour.
            if (drawn.Width > 0 && (drawn.Left <= 0 || drawn.Top <= 0 || drawn.Right >= image.Width || drawn.Bottom >= image.Height))
                continue;
            return FromMask(image, renderedRaster, new PointMm(canvas.X, canvas.Y - 1 / density), "GERBER");
        }
    }

    public IReadOnlyList<DxfContour> FromMask(Mat mask, RasterGeometry raster, PointMm origin, string layer = "EXPOSURE")
    {
        if (mask.Width != raster.WidthPx || mask.Height != raster.HeightPx || mask.NumberOfChannels != 1 || mask.Depth != DepthType.Cv8U ||
            !double.IsFinite(raster.PixelsPerMmX) || !double.IsFinite(raster.PixelsPerMmY) || raster.PixelsPerMmX <= 0 || raster.PixelsPerMmY <= 0)
            throw new ArgumentException("Размер и формат маски не соответствуют геометрии растра.");
        using var copy = mask.Clone();
        using var contours = new VectorOfVectorOfPoint();
        using var hierarchy = new Mat();
        CvInvoke.FindContours(copy, contours, hierarchy, RetrType.List, ChainApproxMethod.ChainApproxSimple);
        var result = new List<DxfContour>();
        for (var i = 0; i < contours.Size; i++)
        {
            using var contour = contours[i];
            var points = contour.ToArray();
            // A single-pixel dot or line has no area in OpenCV's centerline contour representation.
            // Keep it as an open polyline (or a pixel-sized rectangle for an isolated dot).
            if (points.Length == 1)
            {
                var p = Convert(points[0]);
                result.Add(Rectangle(layer, new RectMm(p.X - 0.5 / raster.PixelsPerMmX,
                    p.Y - 0.5 / raster.PixelsPerMmY, 1 / raster.PixelsPerMmX, 1 / raster.PixelsPerMmY)));
            }
            else if (points.Length >= 2)
                result.Add(new DxfPolyline(layer, points.Select(Convert).ToArray(),
                    points.Length >= 3 && Math.Abs(CvInvoke.ContourArea(contour)) > 0));
        }
        if (result.Count == 0) throw new InvalidOperationException("В выбранном источнике нет контуров.");
        return result;

        PointMm Convert(Point point) => new(origin.X + point.X / raster.PixelsPerMmX,
            origin.Y + (raster.HeightPx - point.Y) / raster.PixelsPerMmY);
    }

    private static DxfPolyline Rectangle(string layer, RectMm rectangle) => new(layer,
        [new(rectangle.X, rectangle.Y), new(rectangle.Right, rectangle.Y),
            new(rectangle.Right, rectangle.Top), new(rectangle.X, rectangle.Top)], true);
}
