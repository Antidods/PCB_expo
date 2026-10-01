using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class StencilTests : IDisposable
{
    private readonly string _paste = Path.Combine(Path.GetTempPath(), $"pcbexpo-paste-{Guid.NewGuid():N}.gtp");
    private static readonly PrinterInfo Printer = new("test", "CXDLPV4", 2000, 2400, 200, 120, .1, .05, 1, 1, 255);
    private static readonly RectangleF Board = new(100, 50, 20, 10);

    public StencilTests() => File.WriteAllText(_paste,
        "%FSLAX45Y45*%\n%MOMM*%\n%ADD10C,2*%\nD10*\nX10300000Y5400000D03*\nM02*\n");

    [Theory]
    [InlineData(ExposureMode.TopStencil, false, false, 28, 24)]
    [InlineData(ExposureMode.TopStencil, true, false, 42, 24)]
    [InlineData(ExposureMode.TopStencil, false, true, 28, 26)]
    [InlineData(ExposureMode.BottomStencil, false, false, 122, 24)]
    [InlineData(ExposureMode.BottomStencil, true, false, 108, 24)]
    [InlineData(ExposureMode.BottomStencil, false, true, 122, 26)]
    public void StencilKeepsWhiteBlankBlackPasteAndExactRegistrationHoles(
        ExposureMode mode, bool mirrorX, bool mirrorY, double openingX, double openingY)
    {
        var project = Project(mode);
        project.CurrentTransform.Invert = false; // Stencil polarity is fixed even for a saved project.
        project.CurrentTransform.MirrorX = mirrorX;
        project.CurrentTransform.MirrorY = mirrorY;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);
        Assert.Equal(0, Pixel(mask, new(0, 0), onBlank: false));
        Assert.Equal(255, Pixel(mask, new(70, 60)));
        Assert.Equal(0, Pixel(mask, new(openingX, openingY)));
        foreach (var hole in new BlankLayoutService().MechanicalHoles(project.Blank))
        {
            Assert.Equal(0, Pixel(mask, hole));
            Assert.Equal(0, Pixel(mask, new(hole.X + 1.5, hole.Y)));
            Assert.Equal(0, Pixel(mask, new(hole.X, hole.Y + 1.5)));
            Assert.Equal(255, Pixel(mask, new(hole.X + 3, hole.Y))); // Clearance is not a stencil opening.
        }
        Assert.Single(mask.Boards);
        Assert.Equal(mode == ExposureMode.BottomStencil ? 105 : 25, mask.Boards[0].X);
        Assert.Equal(255, Pixel(mask, new(mask.Boards[0].X - .2, mask.Boards[0].Center.Y)));
    }

    [Fact]
    public void FillStencilSubtractsPasteForEveryCopyWithoutDarkBoardFrames()
    {
        var project = Project(ExposureMode.TopStencil);
        project.Blank.WidthMm = 100; project.Blank.HeightMm = 70;
        project.Panelization = new PanelizationSettings { Mode = PlacementMode.FillBlank, SpacingXmm = 5, SpacingYmm = 5,
            MarginLeftMm = 20, MarginRightMm = 20, MarginBottomMm = 20, MarginTopMm = 20 };
        project.Exposure.StencilCompensationMm = -.2;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);
        Assert.Equal(4, mask.Boards.Count);
        foreach (var board in mask.Boards)
        {
            Assert.Equal(0, Pixel(mask, new(board.X + 3, board.Y + 4)));
            Assert.Equal(255, Pixel(mask, new(board.X + .1, board.Y + .1)));
        }
        Assert.Equal(255, Pixel(mask, new(44, 32))); // Space between copies.
    }

    [Theory]
    [InlineData(.3, 255)]
    [InlineData(-.3, 0)]
    public void PositiveCompensationShrinksDarkPasteOpenings(double compensation, int expected)
    {
        var project = Project(ExposureMode.TopStencil);
        project.Exposure.StencilCompensationMm = compensation;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);
        Assert.Equal(expected, Pixel(mask, new(28.9, 24)));
    }

    [Theory]
    [InlineData("Gerber_TopPasteMaskLayer.GTP", GerberLayerKind.TopPasteMask)]
    [InlineData("Gerber_BottomPasteMaskLayer.GBP", GerberLayerKind.BottomPasteMask)]
    [InlineData("test-F_Paste.gbr", GerberLayerKind.TopPasteMask)]
    [InlineData("test-B_Paste.gbr", GerberLayerKind.BottomPasteMask)]
    public void PasteLayersAreRecognized(string name, GerberLayerKind expected) =>
        Assert.Equal(expected, GerberImportService.Classify(name));

    [Fact]
    public void StencilAndCncSettingsRoundTripAndOldProjectsGetDefaults()
    {
        var project = Project(ExposureMode.BottomStencil);
        project.Exposure.StencilSeconds = 12;
        project.Exposure.StencilCompensationMm = -.02;
        project.CncExport.Vias = false;
        project.CncExport.DrillKinds["holes.drl"] = CncDrillKind.Component;
        var restored = JsonSerializer.Deserialize<ProjectModel>(JsonSerializer.Serialize(project, LocalStorage.JsonOptions), LocalStorage.JsonOptions)!;
        Assert.True(restored.IsBottom); Assert.True(restored.IsStencil);
        Assert.Equal(GerberLayerKind.BottomPasteMask, restored.CurrentLayerKind);
        Assert.Equal(12, restored.CurrentExposureSeconds); Assert.Equal(-.02, restored.CurrentCompensationMm);
        Assert.False(restored.CncExport.Vias);
        Assert.Equal(CncDrillKind.Component, restored.CncExport.DrillKinds["holes.drl"]);
        var old = JsonSerializer.Deserialize<ProjectModel>("{\"Mode\":\"TopCopper\"}", LocalStorage.JsonOptions)!;
        Assert.True(old.CncExport.BoardOutlines); Assert.Equal(0, old.Exposure.StencilSeconds);
    }

    private ProjectModel Project(ExposureMode mode)
    {
        var project = new ProjectModel { Mode = mode, PcbPositionMm = new(25, 20) };
        project.LayerPaths[project.CurrentLayerKind] = _paste;
        return project;
    }

    private static int Pixel(MaskResult mask, PointMm point, bool onBlank = true)
    {
        var coordinates = new CoordinateTransformService();
        var pixel = onBlank ? coordinates.BlankToPixel(point, mask.BlankOnLcd, RasterGeometry.Native(Printer))
            : coordinates.LcdToPixel(point, RasterGeometry.Native(Printer));
        // Use an interior outside-LCD-corner pixel rather than the boundary at Y=0.
        return Marshal.ReadByte(mask.Image.DataPointer, Math.Clamp(pixel.Y, 0, mask.Image.Height - 1) * mask.Image.Step + pixel.X);
    }

    private static ExposureRasterService Service()
    {
        var layout = new BlankLayoutService();
        return new(new CoordinateTransformService(), layout, new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService());
    }

    public void Dispose() => File.Delete(_paste);
}
