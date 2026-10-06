using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Emgu.CV;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class CalibrationRasterTests
{
    [Theory]
    [InlineData(0.2, 20, 10, 4, 2)]
    [InlineData(0.4, 20, 10, 8, 4)]
    [InlineData(0.35, 20, 10, 7, 4)]
    [InlineData(0.2, 10, 20, 2, 4)]
    [InlineData(0.01, 20, 10, 1, 1)]
    public void ThicknessGrowsInwardAndKeepsReferenceDimensions(double thickness,
        int densityX, int densityY, int expectedX, int expectedY)
    {
        var printer = new PrinterInfo("test", "CXDLPV4", (uint)(200 * densityX), (uint)(125 * densityY),
            200, 125, 1.0 / densityX, 1.0 / densityY, 1, 1, 255);
        var blank = new BlankProfile { ServiceLineThicknessMm = thickness };
        var raster = RasterGeometry.Native(printer);
        using var result = Service().Build(new ProjectModel { Mode = ExposureMode.Calibration, Blank = blank },
            printer, raster, null);
        var coordinates = new CoordinateTransformService();
        var pattern = CalibrationPattern.Create(blank);
        PixelPoint Pixel(PointMm point) => coordinates.BlankToPixel(point, result.BlankOnLcd, raster);
        byte Value(int x, int y) => Marshal.ReadByte(result.Image.DataPointer, y * result.Image.Step + x);

        var lineStart = Pixel(pattern.LineStart);
        var lineEnd = Pixel(pattern.LineEnd);
        var lineMiddleX = (lineStart.X + lineEnd.X) / 2;
        var litRows = Enumerable.Range(0, result.Image.Height)
            .Where(y => y < Pixel(new PointMm(pattern.Square.X, pattern.Square.Top)).Y &&
                Value(lineMiddleX, y) == 255).ToArray();
        Assert.Equal(expectedY, litRows.Length);
        Assert.Equal(expectedY, litRows[^1] - litRows[0] + 1);
        foreach (var y in litRows)
        {
            Assert.Equal(0, Value(lineStart.X - 1, y));
            Assert.Equal(0, Value(lineEnd.X + 1, y));
            Assert.All(Enumerable.Range(lineStart.X, 100 * densityX + 1), x => Assert.Equal(255, Value(x, y)));
        }

        var topLeft = Pixel(new PointMm(pattern.Square.X, pattern.Square.Top));
        var bottomRight = Pixel(new PointMm(pattern.Square.Right, pattern.Square.Y));
        var middleX = (topLeft.X + bottomRight.X) / 2;
        var middleY = (topLeft.Y + bottomRight.Y) / 2;
        for (var x = topLeft.X - 1; x <= bottomRight.X + 1; x++)
        {
            var lit = x >= topLeft.X && x < topLeft.X + expectedX ||
                x <= bottomRight.X && x > bottomRight.X - expectedX;
            Assert.Equal(lit ? 255 : 0, Value(x, middleY));
        }
        for (var y = topLeft.Y - 1; y <= bottomRight.Y + 1; y++)
        {
            var lit = y >= topLeft.Y && y < topLeft.Y + expectedY ||
                y <= bottomRight.Y && y > bottomRight.Y - expectedY;
            Assert.Equal(lit ? 255 : 0, Value(middleX, y));
        }
        using var square = new Mat(result.Image, new Rectangle(topLeft.X - 1, topLeft.Y - 1,
            50 * densityX + 3, 50 * densityY + 3));
        var bounds = CvInvoke.BoundingRectangle(square);
        Assert.Equal(new Rectangle(1, 1, 50 * densityX + 1, 50 * densityY + 1), bounds);
        Assert.All(new[] { topLeft, bottomRight, new PixelPoint(topLeft.X, bottomRight.Y),
            new PixelPoint(bottomRight.X, topLeft.Y) }, p => Assert.Equal(255, Value(p.X, p.Y)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(25)]
    public void InvalidThicknessIsReported(double thickness)
    {
        var printer = new PrinterInfo("test", "CXDLPV4", 2000, 1250, 200, 125, 0.1, 0.1, 1, 1, 255);
        var project = new ProjectModel
        {
            Mode = ExposureMode.Calibration, Blank = new BlankProfile { ServiceLineThicknessMm = thickness }
        };
        var error = Assert.Throws<InvalidOperationException>(() =>
            Service().Build(project, printer, RasterGeometry.Native(printer), null));
        Assert.Contains("Толщина служебных линий", error.Message);
    }

    [Fact]
    public void ExistingProjectsAndBlankProfilesKeepThicknessAcrossSerialization()
    {
        var project = JsonSerializer.Deserialize<ProjectModel>(
            "{\"Blank\":{\"AlignmentPointDiameterMm\":0.35}}", LocalStorage.JsonOptions)!;
        Assert.Equal(0.35, project.Blank.ServiceLineThicknessMm);
        project.Blank.ServiceLineThicknessMm = 0.4;
        var json = JsonSerializer.Serialize(project, LocalStorage.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0.4, document.RootElement.GetProperty("Blank").GetProperty("AlignmentPointDiameterMm").GetDouble());
        Assert.Equal(0.4, JsonSerializer.Deserialize<ProjectModel>(json, LocalStorage.JsonOptions)!.Blank.ServiceLineThicknessMm);
        var profiles = JsonSerializer.Deserialize<List<BlankProfile>>(
            "[{\"Name\":\"Existing\",\"AlignmentPointDiameterMm\":0.7}]", LocalStorage.JsonOptions)!;
        Assert.Equal(0.7, Assert.Single(profiles).ServiceLineThicknessMm);
        Assert.Equal(0.2, JsonSerializer.Deserialize<BlankProfile>("{}", LocalStorage.JsonOptions)!.ServiceLineThicknessMm);
    }

    private static ExposureRasterService Service()
    {
        var layout = new BlankLayoutService();
        return new(new CoordinateTransformService(), layout, new PanelizationService(layout),
            new GerberRenderService(), new ExposureMaskService());
    }
}
