using System.Drawing;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class ExposureCalibrationTests
{
    private static readonly PrinterInfo Printer = new("test", "CXDLPV4", 6000, 2500, 200, 125, 1.0 / 30, 0.05, 1, 1, 255);

    [Fact]
    public void DefaultMatrixFitsBlankAndHasIndependentWidthGapAndCompensationAxes()
    {
        var settings = new ExposureCalibrationSettings();
        var pattern = ExposureCalibrationPattern.Create(new BlankProfile(), settings);
        Assert.Equal(60, pattern.Cells.Count);
        Assert.Equal(12, pattern.Rows.Count);
        Assert.InRange(pattern.Bounds.X, 0, 150);
        Assert.InRange(pattern.Bounds.Y, 0, 100);
        Assert.InRange(pattern.Bounds.Right, 0, 150);
        Assert.InRange(pattern.Bounds.Top, 0, 100);
        Assert.Equal(settings.CompensationsMm, pattern.Cells.Where(c => c.Row == 0).Select(c => c.CompensationMm));
        Assert.Contains(pattern.Cells, c => c.LineWidthMm == 0.1 && c.GapMm == 0.05 && c.CompensationMm == 0);
        Assert.Throws<InvalidOperationException>(() => ExposureCalibrationPattern.Create(new BlankProfile { WidthMm = 50 }, settings));
    }

    [Fact]
    public void ParallelZigzagLegsHaveRequestedPerpendicularGapAndRemainInTheirCell()
    {
        var cell = ExposureCalibrationPattern.Create(new BlankProfile(), new ExposureCalibrationSettings()).Cells[0];
        foreach (var vertical in new[] { false, true })
        {
            var paths = cell.Paths(vertical);
            Assert.Equal(6, paths.Count);
            var first = paths[0]; var second = paths[1];
            var dx = first[1].X - first[0].X; var dy = first[1].Y - first[0].Y;
            var distance = Math.Abs(dx * (second[0].Y - first[0].Y) - dy * (second[0].X - first[0].X)) /
                Math.Sqrt(dx * dx + dy * dy);
            Assert.Equal(cell.LineWidthMm + cell.GapMm, distance, 10);
            Assert.All(paths.SelectMany(p => p), p =>
            {
                Assert.InRange(p.X, cell.Bounds.X + cell.LineWidthMm / 2, cell.Bounds.Right - cell.LineWidthMm / 2);
                Assert.InRange(p.Y, cell.Bounds.Y + cell.LineWidthMm / 2, cell.Bounds.Top - cell.LineWidthMm / 2);
            });
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(0)]
    [InlineData(2)]
    public void InvalidWidthsAreRejectedBeforeRendering(double width)
    {
        var settings = new ExposureCalibrationSettings { LineWidthsMm = [width] };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void OldProjectKeepsNewDefaultsAndSeparateCalibrationExposure()
    {
        var restored = JsonSerializer.Deserialize<ProjectModel>("{\"Mode\":\"Calibration\",\"Exposure\":{\"CalibrationSeconds\":17}}", LocalStorage.JsonOptions)!;
        Assert.Equal(17, restored.CurrentExposureSeconds);
        restored.Mode = ExposureMode.ExposureCalibration;
        Assert.Equal(0, restored.CurrentExposureSeconds);
        restored.Exposure.ProcessCalibrationSeconds = 22;
        Assert.Equal(22, restored.CurrentExposureSeconds);
        Assert.Equal(5, restored.ProcessCalibration.CompensationsMm.Count);
        restored.ProcessCalibration.TimesSeconds = [15, 20, 25];
        var roundTrip = JsonSerializer.Deserialize<ProjectModel>(JsonSerializer.Serialize(restored, LocalStorage.JsonOptions), LocalStorage.JsonOptions)!;
        Assert.Equal(restored.ProcessCalibration.TimesSeconds, roundTrip.ProcessCalibration.TimesSeconds);
    }

    [Fact]
    public void SmallCompensationRoundsToZeroOrOneAxisInsteadOfForcingBothAxes()
    {
        using var mat = new Mat(21, 21, DepthType.Cv8U, 1);
        mat.SetTo(new MCvScalar(0));
        CvInvoke.Rectangle(mat, new Rectangle(8, 8, 4, 4), new MCvScalar(255), -1);
        var geometry = new RasterGeometry(21, 21, 0.42, 2.1); // pitches: X=0.02, Y=0.10
        using var original = mat.Clone();
        var service = new ExposureMaskService();
        service.Apply(mat, false, false, false, 0.005, geometry);
        using var difference = new Mat();
        CvInvoke.AbsDiff(original, mat, difference);
        Assert.Equal(0, CvInvoke.CountNonZero(difference));
        service.Apply(mat, false, false, false, 0.02, geometry);
        var bounds = CvInvoke.BoundingRectangle(mat);
        Assert.Equal(CvInvoke.BoundingRectangle(original).Width + 2, bounds.Width);
        Assert.Equal(CvInvoke.BoundingRectangle(original).Height, bounds.Height);
    }

    [Fact]
    public void RenderedZeroCompensationCellHasTwelveUnconnectedContinuousZigzags()
    {
        var project = NewProject([0]);
        var raster = RasterGeometry.Native(Printer);
        using var mask = Service().Build(project, Printer, raster, null);
        var cell = ExposureCalibrationPattern.Create(project.Blank, project.ProcessCalibration).Cells[0];
        using var tile = CellImage(mask, cell, raster);
        Assert.Equal(12, Components(tile));
        using var copy = tile.Clone();
        CvInvoke.Threshold(copy, copy, 0, 255, ThresholdType.Binary);
        using var difference = new Mat();
        CvInvoke.AbsDiff(tile, copy, difference);
        Assert.Equal(0, CvInvoke.CountNonZero(difference));
    }

    [Fact]
    public void DefaultZeroColumnHasNoDigitalBridgesAtHalotTemplatePitch()
    {
        var printer = new PrinterInfo("test", "CXDLPV4", 13320, 5120, 223, 126, 223.0 / 13320, 126.0 / 5120, 1, 1, 255);
        var project = new ProjectModel { Mode = ExposureMode.ExposureCalibration };
        var raster = RasterGeometry.Native(printer);
        using var mask = Service().Build(project, printer, raster, null);
        foreach (var cell in ExposureCalibrationPattern.Create(project.Blank, project.ProcessCalibration).Cells.Where(c => c.CompensationMm == 0))
        {
            using var tile = CellImage(mask, cell, raster);
            Assert.Equal(12, Components(tile));
        }
    }

    [Fact]
    public void MatrixColumnsApplyProductionCompensationAndInversionIsLocal()
    {
        var project = NewProject([-0.05, 0, 0.05]);
        var raster = RasterGeometry.Native(Printer);
        using var mask = Service().Build(project, Printer, raster, null);
        var cells = ExposureCalibrationPattern.Create(project.Blank, project.ProcessCalibration).Cells;
        using var negative = CellImage(mask, cells[0], raster);
        using var zero = CellImage(mask, cells[1], raster);
        using var positive = CellImage(mask, cells[2], raster);
        Assert.True(CvInvoke.CountNonZero(negative) < CvInvoke.CountNonZero(zero));
        Assert.True(CvInvoke.CountNonZero(zero) < CvInvoke.CountNonZero(positive));
        project.CurrentTransform.Invert = true;
        using var inverse = Service().Build(project, Printer, raster, null);
        using var invertedZero = CellImage(inverse, cells[1], raster);
        using var sum = new Mat();
        CvInvoke.BitwiseXor(zero, invertedZero, sum);
        Assert.Equal(sum.Width * sum.Height, CvInvoke.CountNonZero(sum));
        // The background outside the pattern is not illuminated by inversion.
        using var corner = new Mat(inverse.Image, new Rectangle(0, 0, 50, 50));
        Assert.Equal(0, CvInvoke.CountNonZero(corner));
    }

    [Fact]
    public void SeriesWithoutTimesDoesNotCreateDirectory()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"pcbexpo-no-series-{Guid.NewGuid():N}");
        Assert.Throws<InvalidOperationException>(() => new ExposureCalibrationExportService().Export(NewProject([0]), Printer, parent));
        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public void RealTemplateSeriesExportsOneLayerPerTimeAndLeavesProjectUnchanged()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PcbExpo.slnx"))) directory = directory.Parent;
        if (directory is null || !File.Exists(Path.Combine(directory.FullName, "150x100.cxdlpv4"))) return;
        var template = Path.Combine(directory.FullName, "150x100.cxdlpv4");
        var parent = Path.Combine(Path.GetTempPath(), $"pcbexpo-calibration-series-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            var project = NewProject([0]);
            project.TemplatePath = template;
            project.ProcessCalibration.TimesSeconds = [1.23, 2.34];
            project.Exposure.ProcessCalibrationSeconds = 9;
            var before = JsonSerializer.Serialize(project, LocalStorage.JsonOptions);
            var printer = new Cxdlpv4TemplateService().ReadInfo(template);
            var result = new ExposureCalibrationExportService().Export(project, printer, parent);
            Assert.Equal(2, result.Files.Count);
            Assert.All(result.Files, f => { Assert.Equal((uint)1, f.LayerCount); Assert.Equal(0, f.DifferentPixels); });
            Assert.Equal(1.23, result.Files[0].ExposureSeconds, 2);
            Assert.Equal(2.34, result.Files[1].ExposureSeconds, 2);
            Assert.Equal(before, JsonSerializer.Serialize(project, LocalStorage.JsonOptions));
            Assert.Contains("R01C01", File.ReadAllText(Path.Combine(result.Directory, "results.tsv")));
            Assert.True(File.Exists(Path.Combine(result.Directory, "README.txt")));
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(parent, "*", SearchOption.AllDirectories)) File.Delete(path);
            foreach (var path in Directory.EnumerateDirectories(parent)) Directory.Delete(path);
            Directory.Delete(parent);
        }
    }

    private static ProjectModel NewProject(List<double> compensations) => new()
    {
        Mode = ExposureMode.ExposureCalibration,
        ProcessCalibration = new ExposureCalibrationSettings { LineWidthsMm = [0.15], GapsMm = [0.15], CompensationsMm = compensations },
        Exposure = new ExposureSettings { ProcessCalibrationSeconds = 10 }
    };
    private static ExposureRasterService Service()
    {
        var layout = new BlankLayoutService();
        return new ExposureRasterService(new CoordinateTransformService(), layout, new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService());
    }
    private static Mat CellImage(MaskResult mask, ExposureCalibrationCell cell, RasterGeometry raster)
    {
        var coordinates = new CoordinateTransformService();
        var a = coordinates.BlankToPixel(new PointMm(cell.Bounds.X, cell.Bounds.Top), mask.BlankOnLcd, raster);
        var b = coordinates.BlankToPixel(new PointMm(cell.Bounds.Right, cell.Bounds.Y), mask.BlankOnLcd, raster);
        return new Mat(mask.Image, new Rectangle(a.X, a.Y, b.X - a.X, b.Y - a.Y));
    }
    private static int Components(Mat image)
    {
        using var contours = new VectorOfVectorOfPoint(); using var hierarchy = new Mat(); using var copy = image.Clone();
        CvInvoke.FindContours(copy, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);
        return contours.Size;
    }
}
