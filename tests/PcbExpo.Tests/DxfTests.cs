using System.Drawing;
using System.Globalization;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class DxfTests
{
    [Fact]
    public void BlankAndRegistrationUseExactMechanicalGeometry()
    {
        var blank = new BlankProfile();
        var contours = new ContourService();
        var entities = contours.Blank(blank);
        var outline = Assert.IsType<DxfPolyline>(entities[0]);
        Assert.True(outline.Closed);
        Assert.Equal(new PointMm(0, 0), outline.Points[0]);
        Assert.Equal(new PointMm(150, 100), outline.Points[2]);
        var holes = entities.Skip(1).Cast<DxfCircle>().ToArray();
        Assert.Equal(4, holes.Length);
        Assert.All(holes, h => Assert.Equal(2, h.RadiusMm));
        Assert.Contains(holes, h => h.Center == new PointMm(140, 90));
        var points = contours.Registration(blank).Cast<DxfCircle>().ToArray();
        Assert.Equal(5, points.Length);
        Assert.All(points, p => Assert.Equal(0.1, p.RadiusMm));
        Assert.Equal(new PointMm(75, 50), points[0].Center);
    }

    [Fact]
    public void CalibrationDxfAndLcdShareReferenceDimensions()
    {
        var blank = new BlankProfile();
        var contours = new ContourService().Calibration(blank).Cast<DxfPolyline>().ToArray();
        Assert.False(contours[0].Closed);
        Assert.Equal(100, contours[0].Points[1].X - contours[0].Points[0].X);
        Assert.True(contours[1].Closed);
        Assert.Equal(50, contours[1].Points[2].X - contours[1].Points[0].X);
        Assert.Equal(50, contours[1].Points[2].Y - contours[1].Points[0].Y);
        Assert.Throws<InvalidOperationException>(() => CalibrationPattern.Create(new BlankProfile { WidthMm = 109 }));

        var printer = new PrinterInfo("test", "CXDLPV4", 2000, 500, 200, 125, 0.1, 0.25, 1, 1, 255);
        var coordinates = new CoordinateTransformService();
        var layout = new BlankLayoutService();
        var service = new ExposureRasterService(coordinates, layout, new PanelizationService(layout),
            new GerberRenderService(), new ExposureMaskService());
        var raster = RasterGeometry.Native(printer);
        using var mask = service.Build(new ProjectModel { Blank = blank, Mode = ExposureMode.Calibration }, printer, raster, null);
        var pattern = CalibrationPattern.Create(blank);
        var squareTop = coordinates.BlankToPixel(new PointMm(pattern.Square.X, pattern.Square.Top), mask.BlankOnLcd, raster);
        using var square = new Mat(mask.Image, new Rectangle(squareTop.X - 1, squareTop.Y - 1, 503, 203));
        var bounds = CvInvoke.BoundingRectangle(square);
        Assert.Equal(501, bounds.Width); // 500 pixel intervals = 50 mm
        Assert.Equal(201, bounds.Height); // 200 pixel intervals = 50 mm
    }

    [Fact]
    public void RasterContoursKeepHolesAndSeparateXYPitchInBlankCoordinates()
    {
        using var mask = new Mat(100, 200, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        CvInvoke.Rectangle(mask, new Rectangle(40, 20, 80, 60), new MCvScalar(255), -1);
        CvInvoke.Rectangle(mask, new Rectangle(60, 40, 20, 20), new MCvScalar(0), -1);
        var contours = new ContourService().FromMask(mask, new RasterGeometry(200, 100, 100, 100), new PointMm(-10, -5));
        Assert.Equal(2, contours.Count);
        var outer = contours.Cast<DxfPolyline>().OrderByDescending(c => c.Points.Max(p => p.X) - c.Points.Min(p => p.X)).First();
        Assert.True(outer.Closed);
        Assert.Equal(10, outer.Points.Min(p => p.X));
        Assert.Equal(50, outer.Points.Max(p => p.X));
        Assert.Equal(15, outer.Points.Min(p => p.Y));
        Assert.Equal(75, outer.Points.Max(p => p.Y));
        Assert.Equal(81 * 61 - 21 * 21, CvInvoke.CountNonZero(mask)); // source remains intact
    }

    [Fact]
    public void SinglePixelDotsAndLinesAreNotDiscarded()
    {
        using var mask = new Mat(50, 50, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        CvInvoke.Line(mask, new Point(5, 5), new Point(15, 5), new MCvScalar(255), 1);
        CvInvoke.Circle(mask, new Point(30, 30), 0, new MCvScalar(255), -1);
        var contours = new ContourService().FromMask(mask, new RasterGeometry(50, 50, 50, 50), new PointMm(0, 0));
        Assert.Equal(2, contours.Count);
        Assert.Contains(contours, c => c is DxfPolyline { Closed: false, Points.Count: 2 });
        Assert.Contains(contours, c => c is DxfPolyline { Closed: true, Points.Count: 4 });
    }

    [Fact]
    public void GerberOutlinePreservesNonRectangularShapeAndCutout()
    {
        using var input = new TemporaryFile(".gko", "%FSLAX24Y24*%%MOMM*%D10*X100000Y200000D02*" +
            "X300000Y200000D01*X300000Y300000D01*X200000Y300000D01*X200000Y400000D01*" +
            "X100000Y400000D01*X100000Y200000D01*X120000Y220000D02*X160000Y220000D01*" +
            "X140000Y260000D01*X120000Y220000D01*M02*");
        var contours = new GerberOutlineService().Read(input.Path).Cast<DxfPolyline>().ToArray();
        Assert.Equal(2, contours.Length);
        Assert.True(contours[0].Closed);
        Assert.Equal(6, contours[0].Points.Count);
        Assert.Contains(new PointMm(20, 30), contours[0].Points);
        Assert.Equal(new PointMm(10, 20), contours[0].Points[0]);
        Assert.True(contours[1].Closed);
        Assert.Equal(3, contours[1].Points.Count);
    }

    [Theory]
    [InlineData("%FSTAX24Y24*%%MOIN*%D10*X01Y02D02*X02D01*Y03D01*", 25.4, 50.8, 50.8, 76.2)]
    [InlineData("%FSLIX24Y24*%%MOMM*%D10*X10000Y20000D02*X10000D01*Y10000D01*", 1, 2, 2, 3)]
    public void OutlineHandlesInchesTrailingZerosAndModalIncrementalCoordinates(string gerber,
        double x0, double y0, double x1, double y1)
    {
        using var input = new TemporaryFile(".gko", gerber + "M02*");
        var line = Assert.IsType<DxfPolyline>(Assert.Single(new GerberOutlineService().Read(input.Path)));
        Assert.Equal(x0, line.Points[0].X, 8); Assert.Equal(y0, line.Points[0].Y, 8);
        Assert.Equal(x1, line.Points[^1].X, 8); Assert.Equal(y1, line.Points[^1].Y, 8);
        Assert.False(line.Closed);
    }

    [Theory]
    [InlineData("G03", 1)]
    [InlineData("G02", -1)]
    public void OutlineFullCircleRetainsArcBulges(string interpolation, int sign)
    {
        using var input = new TemporaryFile(".gko", "%FSLAX24Y24*%%MOMM*%D10*G75*X100000Y0D02*" +
            interpolation + "X100000Y0I-100000J0D01*M02*");
        var circle = Assert.IsType<DxfPolyline>(Assert.Single(new GerberOutlineService().Read(input.Path)));
        Assert.True(circle.Closed);
        Assert.Equal(4, circle.Points.Count);
        Assert.All(circle.Bulges!, bulge => Assert.Equal(sign * Math.Tan(Math.PI / 8), bulge, 10));
    }

    [Fact]
    public void UnsupportedOutlineTransformFailsInsteadOfExportingWrongGeometry()
    {
        using var input = new TemporaryFile(".gko", "%FSLAX24Y24*%%MOMM*%%LMX*%D10*X0Y0D02*X100000Y0D01*M02*");
        Assert.Throws<InvalidOperationException>(() => new GerberOutlineService().Read(input.Path));
    }

    [Fact]
    public void GerberArtworkKeepsBothEdgesOfThickStrokeAtOriginalCoordinates()
    {
        using var input = new TemporaryFile(".gbr", "%FSLAX24Y24*%\n%MOMM*%\n%ADD10C,1.0*%\nD10*\n" +
            "X100000Y200000D02*\nX300000Y200000D01*\nX300000Y400000D01*\nX100000Y400000D01*\nX100000Y200000D01*\nM02*\n");
        var contours = new ContourService().Gerber(input.Path).Cast<DxfPolyline>().ToArray();
        Assert.Equal(2, contours.Length);
        Assert.All(contours, c => Assert.True(c.Closed));
        var points = contours.SelectMany(c => c.Points).ToArray();
        Assert.InRange(points.Min(p => p.X), 9.48, 9.52);
        Assert.InRange(points.Max(p => p.X), 30.48, 30.52);
        Assert.InRange(points.Min(p => p.Y), 19.48, 19.52);
        Assert.InRange(points.Max(p => p.Y), 40.48, 40.52);
    }

    [Fact]
    public void GerberContourCanvasHandlesFractionalBoundsWithoutChangingScale()
    {
        using var input = new TemporaryFile(".gbr", "%FSLAX45Y45*%\n%MOMM*%\n%ADD10C,0.254*%\nD10*\n" +
            "X0Y2667000D02*\nX3556000Y2667000D01*\nX3556000Y127000D01*\n" +
            "X0Y127000D01*\nX0Y2667000D01*\nM02*\n");
        var contours = new ContourService().Gerber(input.Path).Cast<DxfPolyline>().ToArray();
        Assert.Equal(2, contours.Length);
        var points = contours.SelectMany(c => c.Points).ToArray();
        Assert.InRange(points.Max(p => p.X) - points.Min(p => p.X), 35.78, 35.86);
        Assert.InRange(points.Max(p => p.Y) - points.Min(p => p.Y), 25.62, 25.70);
    }

    [Fact]
    public void LargeIsolatedApertureExpandsCanvasInsteadOfClipping()
    {
        using var input = new TemporaryFile(".gbr", "%FSLAX24Y24*%\n%MOMM*%\n%ADD10C,12.0*%\nD10*\nX200000Y300000D03*\nM02*\n");
        var contour = Assert.IsType<DxfPolyline>(Assert.Single(new ContourService().Gerber(input.Path)));
        Assert.True(contour.Closed);
        Assert.InRange(contour.Points.Min(p => p.X), 13.98, 14.02);
        Assert.InRange(contour.Points.Max(p => p.X), 25.98, 26.02);
        Assert.InRange(contour.Points.Min(p => p.Y), 23.98, 24.02);
        Assert.InRange(contour.Points.Max(p => p.Y), 35.98, 36.02);
    }

    [Fact]
    public void DxfFileUsesMillimetersClosedFlagsAndInvariantNumbersForOneSelectedContour()
    {
        using var output = new TemporaryFile(".dxf", "");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var circle = new ContourService().Registration(new BlankProfile())[0];
            new DxfExportService().Export(output.Path, [circle]);
            var pairs = ReadPairs(output.Path);
            Assert.Contains((9, "$INSUNITS"), pairs);
            Assert.Equal("4", pairs[pairs.IndexOf((9, "$INSUNITS")) + 1].Value);
            Assert.Single(pairs, p => p == (0, "CIRCLE"));
            Assert.Contains((40, "0.1"), pairs);
            Assert.DoesNotContain(pairs, p => p.Value.Contains(','));
            Assert.Equal((0, "EOF"), pairs[^1]);
            new DxfExportService().Export(output.Path, new ContourService().Calibration(new BlankProfile()));
            pairs = ReadPairs(output.Path);
            var flags = pairs.Where(p => p.Code == 70).Select(p => p.Value).TakeLast(2).ToArray();
            Assert.Equal(new[] { "0", "1" }, flags);
            Assert.Equal(2, pairs.Count(p => p == (0, "LWPOLYLINE")));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void InvalidDxfGeometryDoesNotOverwriteExistingFile()
    {
        using var output = new TemporaryFile(".dxf", "original");
        Assert.Throws<InvalidOperationException>(() => new DxfExportService().Export(output.Path,
            [new DxfCircle("HOLES", new PointMm(double.NaN, 0), 2)]));
        Assert.Equal("original", File.ReadAllText(output.Path));
    }

    private static List<(int Code, string Value)> ReadPairs(string path)
    {
        var lines = File.ReadAllLines(path);
        Assert.Equal(0, lines.Length % 2);
        return Enumerable.Range(0, lines.Length / 2).Select(i => (int.Parse(lines[2 * i]), lines[2 * i + 1])).ToList();
    }

    private sealed class TemporaryFile : IDisposable
    {
        public string Path { get; }
        public TemporaryFile(string extension, string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pcbexpo-dxf-{Guid.NewGuid():N}{extension}");
            File.WriteAllText(Path, content);
        }
        public void Dispose() => File.Delete(Path);
    }
}
