using System.Runtime.InteropServices;
using Emgu.CV;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class RegistrationRasterTests
{
    private static readonly PrinterInfo Printer = new("test", "CXDLPV4", 2400, 800, 120, 80, 0.05, 0.1, 1, 1, 255);

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.4)]
    public void RingsAreInsideHolesAndFrameOutsideBlankWithPhysicalWidth(double thickness)
    {
        var blank = new BlankProfile
        {
            WidthMm = 100, HeightMm = 60, ServiceLineThicknessMm = thickness,
            HoleInsetXmm = 10, HoleInsetYmm = 10, AlignmentInsetXmm = 20, AlignmentInsetYmm = 20
        };
        var project = new ProjectModel { Mode = ExposureMode.Registration, Blank = blank };
        var raster = RasterGeometry.Native(Printer);
        using var result = Service().Build(project, Printer, raster, null);
        var coordinates = new CoordinateTransformService();
        int Pixel(double x, double y)
        {
            var p = coordinates.BlankToPixel(new PointMm(x, y), result.BlankOnLcd, raster);
            return Marshal.ReadByte(result.Image.DataPointer, p.Y * result.Image.Step + p.X);
        }
        foreach (var hole in new BlankLayoutService().MechanicalHoles(blank))
        {
            Assert.Equal(0, Pixel(hole.X, hole.Y));
            Assert.Equal(255, Pixel(hole.X + 2 - thickness / 2, hole.Y));
            Assert.Equal(255, Pixel(hole.X, hole.Y + 2 - thickness / 2));
            Assert.Equal(0, Pixel(hole.X + 2 + 0.1, hole.Y));
            Assert.Equal(0, Pixel(hole.X, hole.Y + 2 + 0.1));
            Assert.Equal(0, Pixel(hole.X + 2 - thickness - 0.1, hole.Y));
            Assert.Equal(0, Pixel(hole.X, hole.Y + 2 - thickness - 0.1));
        }
        foreach (var point in new BlankLayoutService().AlignmentPoints(blank))
            Assert.Equal(255, Pixel(point.X, point.Y));
        Assert.Equal(255, Pixel(-thickness / 2, 30));
        Assert.Equal(255, Pixel(100 + thickness / 2, 30));
        Assert.Equal(255, Pixel(50, -thickness / 2));
        Assert.Equal(255, Pixel(50, 60 + thickness / 2));
        Assert.Equal(0, Pixel(0.1, 30));
        Assert.Equal(0, Pixel(99.9, 30));
        Assert.Equal(0, Pixel(50, 0.1));
        Assert.Equal(0, Pixel(50, 59.9));
        Assert.Equal(0, Pixel(-thickness - 0.1, 30));
        Assert.Equal(0, Pixel(50, -thickness - 0.1));
        Assert.Empty(result.Boards);
    }

    [Fact]
    public void DefaultPointsRemainVisibleInCentersOfOutlinedHoles()
    {
        var project = new ProjectModel
        {
            Mode = ExposureMode.Registration,
            Blank = new BlankProfile { WidthMm = 100, HeightMm = 60 }
        };
        using var result = Service().Build(project, Printer, RasterGeometry.Native(Printer), null);
        foreach (var point in new BlankLayoutService().AlignmentPoints(project.Blank))
        {
            var p = new CoordinateTransformService().BlankToPixel(point, result.BlankOnLcd, RasterGeometry.Native(Printer));
            Assert.Equal(255, Marshal.ReadByte(result.Image.DataPointer, p.Y * result.Image.Step + p.X));
        }
    }

    [Fact]
    public void FrameThatDoesNotFitIsReportedInsteadOfClipped()
    {
        var project = new ProjectModel { Mode = ExposureMode.Registration };
        project.Blank.WidthMm = Printer.DisplayWidthMm;
        project.Blank.HeightMm = 60;
        var error = Assert.Throws<InvalidOperationException>(() => Service().Build(project, Printer, RasterGeometry.Native(Printer), null));
        Assert.Contains("рамка", error.Message);
    }

    private static ExposureRasterService Service()
    {
        var layout = new BlankLayoutService();
        return new(new CoordinateTransformService(), layout, new PanelizationService(layout),
            new GerberRenderService(), new ExposureMaskService());
    }
}
