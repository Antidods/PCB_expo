using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Runtime.InteropServices;

namespace PcbExpo.Core;

public sealed class ExposureCalibrationRenderer
{
    private readonly CoordinateTransformService _coordinates = new();
    public void Draw(Mat output, ProjectModel project, RectMm blankOnLcd, RasterGeometry raster)
    {
        var pattern = ExposureCalibrationPattern.Create(project.Blank, project.ProcessCalibration);
        foreach (var cell in pattern.Cells)
        {
            var topLeft = Pixel(new PointMm(cell.Bounds.X, cell.Bounds.Top));
            var bottomRight = Pixel(new PointMm(cell.Bounds.Right, cell.Bounds.Y));
            var rectangle = new Rectangle(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
            using var tile = new Mat(rectangle.Height, rectangle.Width, DepthType.Cv8U, 1);
            var pixels = new byte[rectangle.Height * rectangle.Width];
            foreach (var path in cell.Paths(false).Concat(cell.Paths(true)))
                DrawStroke(pixels, path, cell.LineWidthMm, rectangle);
            Marshal.Copy(pixels, 0, tile.DataPointer, pixels.Length);
            // The same compensation implementation is used by production Gerber masks.
            new ExposureMaskService().Apply(tile, project.CurrentTransform.Invert, false, false, cell.CompensationMm, raster);
            using var destination = new Mat(output, rectangle);
            tile.CopyTo(destination);
        }
        var time = project.CurrentExposureSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Label($"T={time}s  W/G=mm  H/V", new PointMm(pattern.Bounds.X + 2, pattern.Bounds.Top - 3), 2.6);
        foreach (var cell in pattern.Cells.Where(c => c.Row == 0))
        {
            var compensation = cell.CompensationMm.ToString("+0.###;-0.###;0", System.Globalization.CultureInfo.InvariantCulture);
            Label($"C{cell.Column + 1:00}", new PointMm(cell.Bounds.X, pattern.Bounds.Top - 7), 1.8);
            Label(compensation, new PointMm(cell.Bounds.X, pattern.Bounds.Top - 10), 1.8);
        }
        for (var row = 0; row < pattern.Rows.Count; row++)
        {
            var cell = pattern.Cells.First(c => c.Row == row);
            var values = pattern.Rows[row];
            Label($"R{row + 1:00}", new PointMm(pattern.Bounds.X + 1, cell.Bounds.Top - 0.7), 2.0);
            Label(values.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                values.Gap.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                new PointMm(pattern.Bounds.X + 1, cell.Bounds.Top - 3.3), 2.0);
        }

        PixelPoint Pixel(PointMm point) => _coordinates.BlankToPixel(point, blankOnLcd, raster);
        void DrawStroke(byte[] pixels, PointMm[] points, double width, Rectangle tile)
        {
            var radius = width / 2;
            for (var i = 1; i < points.Length; i++)
            {
                var a = points[i - 1]; var b = points[i];
                var first = Pixel(new PointMm(Math.Min(a.X, b.X) - radius, Math.Max(a.Y, b.Y) + radius));
                var last = Pixel(new PointMm(Math.Max(a.X, b.X) + radius, Math.Min(a.Y, b.Y) - radius));
                var dx = b.X - a.X; var dy = b.Y - a.Y;
                var squaredLength = dx * dx + dy * dy;
                // Test physical pixel centers against a round-ended stroke. Rounding polygon vertices
                // before filling can widen the stroke and merge the smallest nominal gaps.
                for (var py = Math.Max(0, first.Y - tile.Y - 1); py <= Math.Min(tile.Height - 1, last.Y - tile.Y + 1); py++)
                {
                    var y = raster.DisplayHeightMm - (tile.Y + py) / raster.PixelsPerMmY - blankOnLcd.Y;
                    for (var px = Math.Max(0, first.X - tile.X - 1); px <= Math.Min(tile.Width - 1, last.X - tile.X + 1); px++)
                    {
                        var x = (tile.X + px) / raster.PixelsPerMmX - blankOnLcd.X;
                        var fraction = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / squaredLength, 0, 1);
                        var distanceX = x - a.X - fraction * dx;
                        var distanceY = y - a.Y - fraction * dy;
                        if (distanceX * distanceX + distanceY * distanceY <= radius * radius)
                            pixels[py * tile.Width + px] = 255;
                    }
                }
            }
        }
        void Label(string text, PointMm baseline, double heightMm)
        {
            // Render text isotropically, then correct X/Y scale to keep physical letter proportions.
            var height = Math.Max(8, heightMm * raster.PixelsPerMmY);
            var scale = height / 22;
            var thickness = Math.Max(1, (int)Math.Round(raster.PixelsPerMmY * 0.15));
            var baseLine = 0;
            var size = CvInvoke.GetTextSize(text, FontFace.HersheySimplex, scale, thickness, ref baseLine);
            using var label = new Mat(size.Height + baseLine + 4, size.Width + 4, DepthType.Cv8U, 1);
            label.SetTo(new MCvScalar(0));
            CvInvoke.PutText(label, text, new Point(2, size.Height + 1), FontFace.HersheySimplex, scale, new MCvScalar(255), thickness, LineType.EightConnected);
            using var stretched = new Mat();
            CvInvoke.Resize(label, stretched, new Size(Math.Max(1, (int)Math.Round(label.Width * raster.PixelsPerMmX / raster.PixelsPerMmY)), label.Height), interpolation: Inter.Nearest);
            var pixel = Pixel(baseline);
            var rectangle = new Rectangle(pixel.X, pixel.Y - size.Height - 1, stretched.Width, stretched.Height);
            if (!new Rectangle(0, 0, output.Width, output.Height).Contains(rectangle))
                throw new InvalidOperationException("Подпись калибровочного теста выходит за пределы LCD.");
            using var destination = new Mat(output, rectangle);
            CvInvoke.BitwiseOr(destination, stretched, destination);
        }
    }
}
