using PcbExpo.App;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class DxfSourceCatalogTests
{
    [Fact]
    public void SourcesWithoutGerberKeepTheirOrderAndBuildWithoutPrinter()
    {
        var project = new ProjectModel();
        var sources = Catalog().Create(project, null, null);

        Assert.Equal(new[] { "blank", "registration", "calibration" }, sources.Select(s => s.FileStem));
        Assert.Equal(5, sources[0].Build().Count);
        Assert.Equal(5, sources[1].Build().Count);
        Assert.Equal(2, sources[2].Build().Count);
    }

    [Fact]
    public void CncShortcutKeepsAllSourcesAndDefersFileReadingUntilSelection()
    {
        var project = new ProjectModel();
        using var package = new GerberPackage
        {
            Layers =
            [
                new("missing-outline.gko", "outline.gko", GerberLayerKind.BoardOutline, "outline.gko"),
                new("missing-drill.drl", "drill.drl", GerberLayerKind.Drill, "drill.drl")
            ]
        };
        project.LayerPaths[GerberLayerKind.BoardOutline] = "missing-outline.gko";
        var sources = Catalog().Create(project, package, null);
        var cncFirst = Catalog().Create(project, package, null, cncFirst: true);

        Assert.Equal("blank", sources[0].FileStem);
        Assert.Equal("cnc_layout", cncFirst[0].FileStem);
        Assert.Equal(new RectMm(0, 0, 150, 100), cncFirst[0].ViewBounds);
        Assert.Equal(sources.Select(s => s.FileStem).Order(), cncFirst.Select(s => s.FileStem).Order());
        Assert.Contains(cncFirst, s => s.FileStem == "board_outline");
        Assert.Contains(cncFirst, s => s.FileStem == "drill");
        Assert.Throws<FileNotFoundException>(() => sources.Single(s => s.FileStem == "drill").Build());
    }

    private static DxfSourceCatalog Catalog()
    {
        var layout = new BlankLayoutService();
        return new DxfSourceCatalog(new ExposureRasterService(new CoordinateTransformService(), layout,
            new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService()));
    }
}
