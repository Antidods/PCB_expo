using System.IO.Compression;
using Emgu.CV;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class RealSourceTests
{
    private static string? SourceFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PcbExpo.slnx")))
            directory = directory.Parent;
        if (directory is null) return null;
        var source = Path.Combine(directory.FullName, "Gerber_Cube_PCB_Cube_2026-09-23");
        return Directory.Exists(source) ? source : null;
    }

    [Fact]
    public void EasyEdaSampleHasExpectedLayersAndOutline()
    {
        var source = SourceFolder();
        if (source is null) return;
        using var package = new GerberImportService().Import(source);
        Assert.Equal(12, package.Layers.Count);
        Assert.Equal(3, package.Layers.Count(x => x.Kind == GerberLayerKind.Drill));
        foreach (var kind in new[] { GerberLayerKind.TopCopper, GerberLayerKind.BottomCopper,
                     GerberLayerKind.TopSolderMask, GerberLayerKind.BottomSolderMask,
                     GerberLayerKind.BoardOutline })
            Assert.Single(package.Layers, x => x.Kind == kind);
        Assert.False(package.OutlineFallback);
        Assert.NotNull(package.BoardBoundsMm);
        Assert.Equal(35.56, package.BoardBoundsMm.Value.Width, 2);
        Assert.Equal(25.40, package.BoardBoundsMm.Value.Height, 2);
    }

    [Fact]
    public void EasyEdaArtworkRendersAperturesAndSolderMaskOpenings()
    {
        var source = SourceFolder();
        if (source is null) return;
        using var package = new GerberImportService().Import(source);
        var raster = new RasterGeometry(1200, 678, 223, 126);
        var renderer = new GerberRenderService();
        using var copper = renderer.RenderBoard(package.GetLayer(GerberLayerKind.TopCopper)!.Path,
            package.BoardBoundsMm!.Value, raster, false);
        using var maskOpenings = renderer.RenderBoard(package.GetLayer(GerberLayerKind.TopSolderMask)!.Path,
            package.BoardBoundsMm.Value, raster, false);
        Assert.True(CvInvoke.CountNonZero(copper) > 1000);
        Assert.True(CvInvoke.CountNonZero(maskOpenings) > 100);
        Assert.True(CvInvoke.CountNonZero(maskOpenings) < copper.Rows * copper.Cols / 2);
    }

    [Fact]
    public void ZipPackageUsesTheSameClassification()
    {
        var source = SourceFolder();
        if (source is null) return;
        var zip = Path.Combine(Path.GetTempPath(), $"pcbexpo-test-{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(source, zip);
            using var package = new GerberImportService().Import(zip);
            Assert.Equal(12, package.Layers.Count);
            Assert.Equal(35.56, package.BoardBoundsMm!.Value.Width, 2);
            Assert.All(package.Layers, layer => Assert.False(Path.IsPathRooted(layer.RelativePath)));
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
        }
    }

    [Fact]
    public void RealArtworkUsesOneLayoutForCopperAndSolderMask()
    {
        var source = SourceFolder();
        if (source is null) return;
        using var package = new GerberImportService().Import(source);
        var project = new ProjectModel();
        project.Panelization.Mode = PlacementMode.FillBlank;
        project.LayerPaths = package.Layers.Where(x => x.Kind is GerberLayerKind.TopCopper or
            GerberLayerKind.BottomCopper or GerberLayerKind.TopSolderMask or GerberLayerKind.BottomSolderMask)
            .ToDictionary(x => x.Kind, x => x.Path);
        var printer = new PrinterInfo("test", "CXDLPV4", 1200, 678, 223, 126,
            223.0 / 1200, 126.0 / 678, 1, 1, 255);
        var coordinates = new CoordinateTransformService();
        var layout = new BlankLayoutService();
        var service = new ExposureRasterService(coordinates, layout, new PanelizationService(layout),
            new GerberRenderService(), new ExposureMaskService());
        var boardsByMode = new Dictionary<ExposureMode, RectMm[]>();
        foreach (var mode in new[] { ExposureMode.TopCopper, ExposureMode.BottomCopper,
                     ExposureMode.TopSolderMask, ExposureMode.BottomSolderMask })
        {
            project.Mode = mode;
            using var result = service.Build(project, printer, RasterGeometry.Preview(printer), package.BoardBoundsMm);
            Assert.True(CvInvoke.CountNonZero(result.Image) > 0);
            boardsByMode[mode] = result.Boards.ToArray();
        }
        Assert.True(boardsByMode[ExposureMode.TopCopper].Length > 1);
        Assert.Equal(boardsByMode[ExposureMode.TopCopper], boardsByMode[ExposureMode.TopSolderMask]);
        Assert.Equal(boardsByMode[ExposureMode.BottomCopper], boardsByMode[ExposureMode.BottomSolderMask]);
        Assert.Equal(boardsByMode[ExposureMode.TopCopper]
            .Select(x => coordinates.FlipBlankAroundVerticalAxis(x, project.Blank)),
            boardsByMode[ExposureMode.BottomCopper]);
    }
}
