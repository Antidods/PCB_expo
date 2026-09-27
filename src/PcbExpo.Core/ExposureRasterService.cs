using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using UVtools.Core.Gerber;

namespace PcbExpo.Core;

public sealed class GerberRenderService
{
    public Mat RenderBoard(string gerberPath, RectangleF boardBounds, RasterGeometry raster, bool antiAliasing)
    {
        if (!File.Exists(gerberPath)) throw new FileNotFoundException("Не найден выбранный Gerber.", gerberPath);
        var width = Math.Max(1, CoordinateTransformService.MmToPx(boardBounds.Width, raster.PixelsPerMmX));
        var height = Math.Max(1, CoordinateTransformService.MmToPx(boardBounds.Height, raster.PixelsPerMmY));
        var mat = new Mat(height, width, DepthType.Cv8U, 1);
        mat.SetTo(new MCvScalar(0));
        try
        {
            GerberFormat.ParseAndDraw(gerberPath, mat,
                new SizeF((float)raster.PixelsPerMmX, (float)raster.PixelsPerMmY),
                offset: new SizeF(-boardBounds.X, -boardBounds.Y),
                enableAntiAliasing: antiAliasing);
            // Gerber Y растёт вверх, строки bitmap растут вниз.
            CvInvoke.Flip(mat, mat, FlipType.Vertical);
            return mat;
        }
        catch
        {
            mat.Dispose();
            throw;
        }
    }
}

public sealed class ExposureMaskService
{
    public void Apply(Mat mask, bool invert, bool mirrorX, bool mirrorY,
        double compensationMm, RasterGeometry raster)
    {
        if (invert) CvInvoke.BitwiseNot(mask, mask);
        if (mirrorX) CvInvoke.Flip(mask, mask, FlipType.Horizontal);
        if (mirrorY) CvInvoke.Flip(mask, mask, FlipType.Vertical);
        if (compensationMm != 0)
        {
            var rx = Math.Max(1, CoordinateTransformService.MmToPx(Math.Abs(compensationMm), raster.PixelsPerMmX));
            var ry = Math.Max(1, CoordinateTransformService.MmToPx(Math.Abs(compensationMm), raster.PixelsPerMmY));
            using var kernel = CvInvoke.GetStructuringElement(MorphShapes.Ellipse, new Size(2 * rx + 1, 2 * ry + 1), new Point(rx, ry));
            CvInvoke.MorphologyEx(mask, mask,
                compensationMm > 0 ? MorphOp.Dilate : MorphOp.Erode, kernel,
                new Point(-1, -1), 1, BorderType.Constant, new MCvScalar(0));
        }
        CvInvoke.Threshold(mask, mask, 127, 255, ThresholdType.Binary);
    }
}

public sealed class MaskResult(Mat image, RectMm blankOnLcd, IReadOnlyList<RectMm> boards) : IDisposable
{
    public Mat Image { get; } = image;
    public RectMm BlankOnLcd { get; } = blankOnLcd;
    public IReadOnlyList<RectMm> Boards { get; } = boards;
    public void Dispose() => Image.Dispose();
}

public sealed class ExposureRasterService(
    CoordinateTransformService coordinates,
    BlankLayoutService blankLayout,
    PanelizationService panelization,
    GerberRenderService gerber,
    ExposureMaskService maskService)
{
    public MaskResult Build(ProjectModel project, PrinterInfo printer, RasterGeometry raster,
        RectangleF? boardBounds)
    {
        var blankOnLcd = coordinates.CenterBlank(printer, project.Blank);
        var output = new Mat(raster.HeightPx, raster.WidthPx, DepthType.Cv8U, 1);
        output.SetTo(new MCvScalar(0));
        try
        {
            if (project.Mode == ExposureMode.Registration)
            {
                DrawRegistration(output, project.Blank, blankOnLcd, raster);
                return new MaskResult(output, blankOnLcd, []);
            }
            if (project.Mode == ExposureMode.Calibration)
            {
                DrawCalibration(output, project.Blank, blankOnLcd, raster);
                return new MaskResult(output, blankOnLcd, []);
            }
            if (boardBounds is null)
                throw new InvalidOperationException("Для экспозиции платы нужен контур платы.");
            if (!project.LayerPaths.TryGetValue(project.CurrentLayerKind, out var path))
                throw new InvalidOperationException("Для выбранного режима экспозиции не назначен слой Gerber.");

            var boards = panelization.Layout(boardBounds.Value.Width, boardBounds.Value.Height,
                project.Blank, project.PcbPositionMm, project.Panelization);
            using var local = gerber.RenderBoard(path, boardBounds.Value, raster, project.AntiAliasing);
            var transform = project.CurrentTransform;
            maskService.Apply(local, transform.Invert, transform.MirrorX, transform.MirrorY,
                project.CurrentCompensationMm, raster);
            if (project.IsBottom)
                CvInvoke.Flip(local, local, FlipType.Horizontal);

            var physicalBoards = project.IsBottom
                ? boards.Select(b => coordinates.FlipBlankAroundVerticalAxis(b, project.Blank)).ToArray()
                : boards.ToArray();
            foreach (var board in physicalBoards)
            {
                var lcd = coordinates.BlankToLcd(board, blankOnLcd);
                var topLeft = coordinates.LcdToPixel(new PointMm(lcd.X, lcd.Top), raster);
                Paste(local, output, topLeft);
            }
            return new MaskResult(output, blankOnLcd, physicalBoards);
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private static void Paste(Mat local, Mat output, PixelPoint topLeft)
    {
        var destination = new Rectangle(topLeft.X, topLeft.Y, local.Width, local.Height);
        var clipped = Rectangle.Intersect(destination, new Rectangle(0, 0, output.Width, output.Height));
        if (clipped.Width != local.Width || clipped.Height != local.Height)
            throw new InvalidOperationException("Экспозиционная маска вышла за пределы LCD.");
        using var target = new Mat(output, clipped);
        CvInvoke.BitwiseOr(target, local, target);
    }

    private void DrawRegistration(Mat output, BlankProfile blank, RectMm blankOnLcd, RasterGeometry raster)
    {
        foreach (var point in blankLayout.AlignmentPoints(blank))
        {
            var center = coordinates.BlankToPixel(point, blankOnLcd, raster);
            var radiusX = Math.Max(1, CoordinateTransformService.MmToPx(blank.AlignmentPointDiameterMm / 2, raster.PixelsPerMmX));
            var radiusY = Math.Max(1, CoordinateTransformService.MmToPx(blank.AlignmentPointDiameterMm / 2, raster.PixelsPerMmY));
            var rect = new Rectangle(center.X - radiusX, center.Y - radiusY, radiusX * 2 + 1, radiusY * 2 + 1);
            if (!new Rectangle(0, 0, output.Width, output.Height).Contains(rect))
                throw new InvalidOperationException("Точка центровки выходит за пределы LCD.");
            CvInvoke.Ellipse(output, new Point(center.X, center.Y), new Size(radiusX, radiusY),
                0, 0, 360, new MCvScalar(255), -1, LineType.EightConnected);
        }
    }

    private void DrawCalibration(Mat output, BlankProfile blank, RectMm blankOnLcd, RasterGeometry raster)
    {
        if (blank.WidthMm < 110 || blank.HeightMm < 70)
            throw new InvalidOperationException("Для калибровочного рисунка нужна заготовка не меньше 110 × 70 мм.");
        var center = new PointMm(blank.WidthMm / 2, blank.HeightMm / 2);
        var lineA = coordinates.BlankToPixel(new PointMm(center.X - 50, center.Y + 28), blankOnLcd, raster);
        var lineB = coordinates.BlankToPixel(new PointMm(center.X + 50, center.Y + 28), blankOnLcd, raster);
        CvInvoke.Line(output, new Point(lineA.X, lineA.Y), new Point(lineB.X, lineB.Y), new MCvScalar(255), 1, LineType.EightConnected);
        var squareA = coordinates.BlankToPixel(new PointMm(center.X - 25, center.Y - 26), blankOnLcd, raster);
        var squareB = coordinates.BlankToPixel(new PointMm(center.X + 25, center.Y + 24), blankOnLcd, raster);
        CvInvoke.Rectangle(output, new Rectangle(squareA.X, squareB.Y, squareB.X - squareA.X, squareA.Y - squareB.Y), new MCvScalar(255), 1, LineType.EightConnected);
    }
}


