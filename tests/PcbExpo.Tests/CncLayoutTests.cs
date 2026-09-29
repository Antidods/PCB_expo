using System.Drawing;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class CncLayoutTests : IDisposable
{
    private readonly List<string> _files = [];

    [Fact]
    public void ExcellonKeepsDiametersModalCoordinatesAndTrueSlotArcs()
    {
        var path = Drill("METRIC,LZ", "T01C0.8\nT02C1.0", "T01\nX10.0Y20.0\nX11.0\nT02\nX12.0Y21.0G85X15.0Y21.0");
        var contours = new ExcellonDrillService().Read(path);
        Assert.Equal(3, contours.Count);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(10, 20), 0.4), contours[0]);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(11, 20), 0.4), contours[1]);
        var slot = Assert.IsType<DxfPolyline>(contours[2]);
        Assert.True(slot.Closed);
        Assert.Equal(new double[] { 0, -1, 0, -1 }, slot.Bulges);
        var points = DxfGeometry.Points(slot);
        Assert.Equal(11.5, points.Min(p => p.X), 8);
        Assert.Equal(15.5, points.Max(p => p.X), 8);
        Assert.Equal(20.5, points.Min(p => p.Y), 8);
        Assert.Equal(21.5, points.Max(p => p.Y), 8);
    }

    [Theory]
    [InlineData("METRIC,TZ,000.000", "X123Y2500", 0.123, 2.5)]
    [InlineData("METRIC,LZ,000.000", "X0012Y0025", 1.2, 2.5)]
    [InlineData("INCH,TZ,00.0000", "X10000Y5000", 25.4, 12.7)]
    [InlineData("INCH,LZ", "X1.0Y0.5", 25.4, 12.7)]
    public void DeclaredCoordinateFormatAndUnitsAreRespected(string unit, string coordinates, double x, double y)
    {
        var path = Drill(unit, "T01C0.1", "T01\n" + coordinates);
        var hole = Assert.IsType<DxfCircle>(Assert.Single(new ExcellonDrillService().Read(path)));
        Assert.Equal(x, hole.Center.X, 10); Assert.Equal(y, hole.Center.Y, 10);
        Assert.Equal(unit.StartsWith("INCH") ? 1.27 : 0.05, hole.RadiusMm, 10);
    }

    [Fact]
    public void FileFormatCommentAndIncrementalCoordinatesAreRespected()
    {
        var path = Drill("METRIC,TZ\n;FILE_FORMAT=3:3", "T01C1", "T01\nG91\nX1000Y2000\nX500Y-1000");
        var holes = new ExcellonDrillService().Read(path).Cast<DxfCircle>().ToArray();
        Assert.Equal(new PointMm(1, 2), holes[0].Center);
        Assert.Equal(new PointMm(1.5, 1), holes[1].Center);
    }

    [Fact]
    public void StraightRoutedSlotDoesNotCreateHoleAtRapidMove()
    {
        var path = Drill("METRIC,LZ", "T01C1", "T01\nG00X2.0Y3.0\nM15\nG01X6.0Y3.0\nM16\nG05\nX10.0Y10.0");
        var contours = new ExcellonDrillService().Read(path);
        Assert.Equal(2, contours.Count);
        Assert.IsType<DxfPolyline>(contours[0]);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(10, 10), 0.5), contours[1]);
    }

    [Theory]
    [InlineData("METRIC,LZ", "T01\nX100Y200")]
    [InlineData("METRIC,LZ", "T02\nX1.0Y2.0")]
    [InlineData("METRIC,LZ", "T01\nX1.0Y2.0\nG02X3.0Y4.0I1.0J0.0")]
    [InlineData("METRIC,LZ", "T01\nX1.0Y2.0\nM00\nX3.0Y4.0")]
    [InlineData("METRIC,LZ", "T01\nX1.0Y2.0\nR2X1.0Y0.0")]
    [InlineData("METRIC,LZ", "T01\nX1.0Y2.0\nX3.0Y4.0BAD")]
    [InlineData("METRIC,LZ", "T01\nX1.0Y2.0\nG01X3.0Y4.0")]
    public void AmbiguousOrUnsupportedDrillDataIsNotSilentlyDropped(string unit, string commands)
    {
        var path = Drill(unit, "T01C0.8", commands);
        var error = Assert.Throws<InvalidDataException>(() => new ExcellonDrillService().Read(path));
        Assert.Contains(Path.GetFileName(path), error.Message);
    }

    [Fact]
    public void CncOutlineAndDrillsShareSourceOriginAndBoardPlacement()
    {
        var (project, package) = Layout();
        var result = new CncLayoutService().Build(project, package);
        var outline = Assert.IsType<DxfPolyline>(result[0]);
        Assert.Equal(new PointMm(25, 20), outline.Points[0]);
        Assert.Contains(new PointMm(45, 30), outline.Points);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(28, 24), 0.4), result[1]);
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData(false, false, false, 28, 24)]
    [InlineData(false, true, false, 42, 24)]
    [InlineData(false, false, true, 28, 26)]
    [InlineData(true, false, false, 122, 24)]
    [InlineData(true, true, false, 108, 24)]
    [InlineData(true, false, true, 122, 26)]
    public void CncBottomAndLocalMirrorsPreserveHoleSizeAndArcDirection(bool bottom, bool mirrorX, bool mirrorY, double x, double y)
    {
        var (project, package) = Layout();
        project.Mode = bottom ? ExposureMode.BottomCopper : ExposureMode.TopCopper;
        project.CurrentTransform.MirrorX = mirrorX; project.CurrentTransform.MirrorY = mirrorY;
        project.CurrentTransform.Invert = true; project.Exposure.CopperCompensationMm = 0.2;
        var result = new CncLayoutService().Build(project, package);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(x, y), 0.4), result[1]);
        var slot = Assert.IsType<DxfPolyline>(result[2]);
        Assert.Equal(bottom ^ mirrorX ^ mirrorY ? 1 : -1, slot.Bulges![1]);
        Assert.Equal(1, DxfGeometry.Points(slot).Max(p => p.Y) - DxfGeometry.Points(slot).Min(p => p.Y), 8);
    }

    [Fact]
    public void FillBlankDuplicatesTheOutlineAndDrillsAtEveryAcceptedPosition()
    {
        var (project, package) = Layout();
        project.Blank.WidthMm = 100; project.Blank.HeightMm = 70;
        project.Panelization = new PanelizationSettings { Mode = PlacementMode.FillBlank, SpacingXmm = 5, SpacingYmm = 5,
            MarginLeftMm = 20, MarginRightMm = 20, MarginBottomMm = 20, MarginTopMm = 20 };
        var result = new CncLayoutService().Build(project, package);
        Assert.Equal(12, result.Count);
        Assert.Equal(new PointMm[] { new(23, 24), new(48, 24), new(23, 39), new(48, 39) },
            result.OfType<DxfCircle>().Select(c => c.Center));
        Assert.Equal(4, result.OfType<DxfPolyline>().Count(c => c.Layer == "BOARD_OUTLINE"));
    }

    [Fact]
    public void AllDrillFilesAreCombinedAndManualAssignmentSelectsOnlyOne()
    {
        var (project, package) = Layout();
        var second = Drill("METRIC,LZ", "T01C1", "T01\nX110.0Y55.0");
        package.Layers.Add(new(second, Path.GetFileName(second), GerberLayerKind.Drill, "npth.drl"));
        Assert.Equal(4, new CncLayoutService().Build(project, package).Count);
        project.LayerPaths[GerberLayerKind.Drill] = second;
        var hole = Assert.IsType<DxfCircle>(Assert.Single(new CncLayoutService().Build(project, package, includeOutline: false)));
        Assert.Equal(new PointMm(35, 25), hole.Center);
    }

    [Fact]
    public void CncPreviewDoesNotModifyProjectAndCalibrationModeUsesTopGeometry()
    {
        var (project, package) = Layout();
        project.Transformations.Clear();
        project.Mode = ExposureMode.ExposureCalibration;
        var before = System.Text.Json.JsonSerializer.Serialize(project, LocalStorage.JsonOptions);
        var result = new CncLayoutService().Build(project, package);
        Assert.Equal(new DxfCircle("DRILL", new PointMm(28, 24), 0.4), result[1]);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(project, LocalStorage.JsonOptions));
    }

    [Fact]
    public void MisalignedDrillOriginAndMissingOutlineAreRejected()
    {
        var (project, package) = Layout();
        var second = Drill("METRIC,LZ", "T01C1", "T01\nX0.0Y0.0");
        project.LayerPaths[GerberLayerKind.Drill] = second;
        Assert.Throws<InvalidOperationException>(() => new CncLayoutService().Build(project, package));
        project.LayerPaths.Remove(GerberLayerKind.Drill);
        project.LayerPaths.Remove(GerberLayerKind.BoardOutline);
        Assert.Throws<InvalidOperationException>(() => new CncLayoutService().Build(project, package));
    }

    [Fact]
    public void DxfContainsPlacedCirclesAndArcBulgesInMillimeters()
    {
        var (project, package) = Layout();
        var path = Temporary("dxf");
        new DxfExportService().Export(path, new CncLayoutService().Build(project, package));
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains("9\n$INSUNITS\n70\n4\n", text);
        Assert.Contains("10\n28\n20\n24\n30\n0\n40\n0.4\n", text);
        Assert.Contains("8\nDRILL_SLOTS\n", text);
        Assert.Contains("42\n-1\n", text);
    }

    private (ProjectModel Project, GerberPackage Package) Layout()
    {
        var outline = Temporary("gko");
        File.WriteAllText(outline, "%FSLAX45Y45*%\n%MOMM*%\n%ADD10C,0.1*%\nD10*\n" +
            "X10000000Y5000000D02*\nX12000000Y5000000D01*\nX12000000Y6000000D01*\nX10000000Y6000000D01*\nX10000000Y5000000D01*\nM02*\n");
        var drill = Drill("METRIC,LZ", "T01C0.8\nT02C1", "T01\nX103.0Y54.0\nT02\nX107.0Y52.0G85X109.0Y52.0");
        var project = new ProjectModel { PcbPositionMm = new PointMm(25, 20) };
        project.LayerPaths[GerberLayerKind.BoardOutline] = outline;
        var package = new GerberPackage { BoardBoundsMm = new RectangleF(100, 50, 20, 10),
            Layers = [new(outline, "outline.gko", GerberLayerKind.BoardOutline, "outline.gko"),
                new(drill, "pth.drl", GerberLayerKind.Drill, "pth.drl")] };
        return (project, package);
    }
    private string Drill(string units, string tools, string commands)
    {
        var path = Temporary("drl");
        File.WriteAllText(path, $"M48\n{units}\n{tools}\n%\n{commands}\nM30\n");
        return path;
    }
    private string Temporary(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcbexpo-cnc-test-{Guid.NewGuid():N}.{extension}");
        _files.Add(path); return path;
    }
    public void Dispose()
    {
        foreach (var path in _files) if (File.Exists(path)) File.Delete(path);
    }
}
