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
            var rx = Math.Max(0, CoordinateTransformService.MmToPx(Math.Abs(compensationMm), raster.PixelsPerMmX));
            var ry = Math.Max(0, CoordinateTransformService.MmToPx(Math.Abs(compensationMm), raster.PixelsPerMmY));
            // OpenCV's ellipse degenerates to its center for a one-row kernel; a line is needed for a single-axis shift.
            var shape = rx == 0 || ry == 0 ? MorphShapes.Rectangle : MorphShapes.Ellipse;
            using var kernel = CvInvoke.GetStructuringElement(shape, new Size(2 * rx + 1, 2 * ry + 1), new Point(rx, ry));
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
    private const double BoardContourWidthMm = 0.5;

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
            if (project.Mode == ExposureMode.ExposureCalibration)
            {
                new ExposureCalibrationRenderer().Draw(output, project, blankOnLcd, raster);
                return new MaskResult(output, blankOnLcd, []);
            }
            if (boardBounds is null)
                throw new InvalidOperationException("Для экспозиции платы нужен контур платы.");
            if (!project.LayerPaths.TryGetValue(project.CurrentLayerKind, out var path))
                throw new InvalidOperationException("Для выбранного режима экспозиции не назначен слой Gerber.");

            var physicalBoards = panelization.LayoutPhysical(project, boardBounds.Value.Width, boardBounds.Value.Height);
            using var local = gerber.RenderBoard(path, boardBounds.Value, raster, project.AntiAliasing);
            var transform = project.CurrentTransform;
            if (project.IsStencil)
            {
                // Компенсируем окна до инверсии, чтобы эрозия не добавляла тёмную рамку платы.
                maskService.Apply(local, false, transform.MirrorX, transform.MirrorY,
                    -project.CurrentCompensationMm, raster);
                CvInvoke.BitwiseNot(local, local);
            }
            else
                maskService.Apply(local, transform.Invert, transform.MirrorX, transform.MirrorY,
                    project.CurrentCompensationMm, raster);
            if (project.IsBottom)
                CvInvoke.Flip(local, local, FlipType.Horizontal);

            var corner = coordinates.BlankToPixel(new PointMm(0, project.Blank.HeightMm), blankOnLcd, raster);
            var end = coordinates.BlankToPixel(new PointMm(project.Blank.WidthMm, 0), blankOnLcd, raster);
            var blankRectangle = Rectangle.Intersect(new Rectangle(corner.X, corner.Y, end.X - corner.X, end.Y - corner.Y),
                new Rectangle(0, 0, output.Width, output.Height));
            using (var blankArea = new Mat(output, blankRectangle))
                blankArea.SetTo(new MCvScalar(255));

            var boardAreas = physicalBoards.Select(board => BoardArea(local, output, board, blankOnLcd, raster)).ToArray();
            if (!project.IsStencil)
            {
                var contourX = CoordinateTransformService.MmToPx(BoardContourWidthMm, raster.PixelsPerMmX);
                var contourY = CoordinateTransformService.MmToPx(BoardContourWidthMm, raster.PixelsPerMmY);
                foreach (var area in boardAreas)
                {
                    // Тёмный контур расположен снаружи платы; рисунки всех плат накладываются после его построения.
                    var contourArea = Rectangle.Intersect(Rectangle.Inflate(area, contourX, contourY), blankRectangle);
                    using var target = new Mat(output, contourArea);
                    target.SetTo(new MCvScalar(0));
                }
            }
            foreach (var area in boardAreas)
            {
                using var target = new Mat(output, area);
                if (project.IsStencil) CvInvoke.BitwiseAnd(target, local, target);
                else CvInvoke.BitwiseOr(target, local, target);
            }
            foreach (var hole in blankLayout.MechanicalHoles(project.Blank))
            {
                var center = coordinates.BlankToPixel(hole, blankOnLcd, raster);
                var rx = Math.Max(1, CoordinateTransformService.MmToPx(project.Blank.RegistrationHoleDiameterMm / 2, raster.PixelsPerMmX));
                var ry = Math.Max(1, CoordinateTransformService.MmToPx(project.Blank.RegistrationHoleDiameterMm / 2, raster.PixelsPerMmY));
                CvInvoke.Ellipse(output, new Point(center.X, center.Y), new Size(rx, ry),
                    0, 0, 360, new MCvScalar(0), -1, LineType.EightConnected);
            }
            return new MaskResult(output, blankOnLcd, physicalBoards);
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    private Rectangle BoardArea(Mat local, Mat output, RectMm board, RectMm blankOnLcd, RasterGeometry raster)
    {
        var lcd = coordinates.BlankToLcd(board, blankOnLcd);
        var topLeft = coordinates.LcdToPixel(new PointMm(lcd.X, lcd.Top), raster);
        var destination = new Rectangle(topLeft.X, topLeft.Y, local.Width, local.Height);
        var clipped = Rectangle.Intersect(destination, new Rectangle(0, 0, output.Width, output.Height));
        if (clipped.Width != local.Width || clipped.Height != local.Height)
            throw new InvalidOperationException("Экспозиционная маска вышла за пределы LCD.");
        return clipped;
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
        var pattern = CalibrationPattern.Create(blank);
        var lineA = coordinates.BlankToPixel(pattern.LineStart, blankOnLcd, raster);
        var lineB = coordinates.BlankToPixel(pattern.LineEnd, blankOnLcd, raster);
        CvInvoke.Line(output, new Point(lineA.X, lineA.Y), new Point(lineB.X, lineB.Y), new MCvScalar(255), 1, LineType.EightConnected);
        var squareA = coordinates.BlankToPixel(new PointMm(pattern.Square.X, pattern.Square.Y), blankOnLcd, raster);
        var squareB = coordinates.BlankToPixel(new PointMm(pattern.Square.Right, pattern.Square.Top), blankOnLcd, raster);
        // Use the same four reference points as the DXF square.
        CvInvoke.Polylines(output, [new Point(squareA.X, squareA.Y), new Point(squareA.X, squareB.Y),
            new Point(squareB.X, squareB.Y), new Point(squareB.X, squareA.Y)], true,
            new MCvScalar(255), 1, LineType.EightConnected);
    }
}


