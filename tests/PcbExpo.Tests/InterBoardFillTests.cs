using System.Drawing;
using System.Runtime.InteropServices;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class InterBoardFillTests : IDisposable
{
    private readonly string _artwork = Path.Combine(Path.GetTempPath(), $"pcbexpo-fill-{Guid.NewGuid():N}.gbr");
    private static readonly PrinterInfo Printer = new("test", "CXDLPV4", 2000, 2400, 200, 120, .1, .05, 1, 1, 255);
    private static readonly RectangleF Board = new(100, 50, 20, 10);

    public InterBoardFillTests() => File.WriteAllText(_artwork,
        "%FSLAX45Y45*%\n%MOMM*%\n%ADD10C,2*%\nD10*\nX10300000Y5400000D03*\nM02*\n");

    [Theory]
    [InlineData(ExposureMode.TopCopper, false, false, false, 28, 24)]
    [InlineData(ExposureMode.TopCopper, true, false, false, 42, 24)]
    [InlineData(ExposureMode.TopCopper, false, true, false, 28, 26)]
    [InlineData(ExposureMode.BottomCopper, false, false, false, 122, 24)]
    [InlineData(ExposureMode.BottomCopper, true, false, false, 108, 24)]
    [InlineData(ExposureMode.BottomCopper, false, true, false, 122, 26)]
    [InlineData(ExposureMode.TopSolderMask, false, false, true, 28, 24)]
    [InlineData(ExposureMode.TopSolderMask, true, false, true, 42, 24)]
    [InlineData(ExposureMode.TopSolderMask, false, true, true, 28, 26)]
    [InlineData(ExposureMode.BottomSolderMask, false, false, true, 122, 24)]
    [InlineData(ExposureMode.BottomSolderMask, true, false, true, 108, 24)]
    [InlineData(ExposureMode.BottomSolderMask, false, true, true, 122, 26)]
    [InlineData(ExposureMode.TopCopper, false, false, true, 28, 24)]
    [InlineData(ExposureMode.BottomCopper, false, false, true, 122, 24)]
    [InlineData(ExposureMode.TopSolderMask, false, false, false, 28, 24)]
    [InlineData(ExposureMode.BottomSolderMask, false, false, false, 122, 24)]
    public void FreeSpaceIsWhiteAndHolesStayDarkWhileBoardTransformIsPreserved(
        ExposureMode mode, bool mirrorX, bool mirrorY, bool invert, double featureX, double featureY)
    {
        var project = Project(mode);
        project.CurrentTransform.Invert = invert;
        project.CurrentTransform.MirrorX = mirrorX;
        project.CurrentTransform.MirrorY = mirrorY;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);

        Assert.Equal(invert ? 0 : 255, Pixel(mask, new(featureX, featureY)));
        var board = Assert.Single(mask.Boards);
        Assert.Equal(invert ? 255 : 0, Pixel(mask, board.Center));
        Assert.Equal(0, Pixel(mask, new(board.X - .2, board.Center.Y)));
        Assert.Equal(0, Pixel(mask, new(board.Right + .2, board.Center.Y)));
        Assert.Equal(0, Pixel(mask, new(board.Center.X, board.Y - .2)));
        Assert.Equal(0, Pixel(mask, new(board.Center.X, board.Top + .2)));
        Assert.Equal(0, Pixel(mask, new(board.X - .3, board.Y - .3)));
        Assert.Equal(255, Pixel(mask, new(70, 60)));
        Assert.Equal(255, Pixel(mask, new(.1, 50)));
        Assert.Equal(255, Pixel(mask, new(149.9, 50)));
        Assert.Equal(255, Pixel(mask, new(75, .1)));
        Assert.Equal(255, Pixel(mask, new(75, 99.9)));
        Assert.Equal(0, Pixel(mask, new(-.1, 50)));
        Assert.Equal(0, Pixel(mask, new(150.1, 50)));
        Assert.Equal(0, Pixel(mask, new(75, -.1)));
        Assert.Equal(0, Pixel(mask, new(75, 100.1)));
        foreach (var hole in new BlankLayoutService().MechanicalHoles(project.Blank))
        {
            Assert.Equal(0, Pixel(mask, hole));
            Assert.Equal(0, Pixel(mask, new(hole.X + 1.5, hole.Y)));
            Assert.Equal(0, Pixel(mask, new(hole.X, hole.Y + 1.5)));
            Assert.Equal(255, Pixel(mask, new(hole.X + 3, hole.Y)));
        }
    }

    [Theory]
    [InlineData(ExposureMode.TopCopper)]
    [InlineData(ExposureMode.BottomCopper)]
    [InlineData(ExposureMode.TopSolderMask)]
    [InlineData(ExposureMode.BottomSolderMask)]
    public void PanelFillsGapsAndMarginsWithoutFillingTheBoardArtwork(ExposureMode mode)
    {
        var project = Project(mode);
        project.Blank.WidthMm = 100;
        project.Blank.HeightMm = 70;
        project.Panelization = new PanelizationSettings
        {
            Mode = PlacementMode.FillBlank, SpacingXmm = 5, SpacingYmm = 5,
            MarginLeftMm = 20, MarginRightMm = 20, MarginBottomMm = 20, MarginTopMm = 20
        };
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);

        Assert.Equal(4, mask.Boards.Count);
        var invert = project.CurrentTransform.Invert;
        foreach (var board in mask.Boards)
        {
            var featureX = project.IsBottom ? board.Right - 3 : board.X + 3;
            Assert.Equal(invert ? 0 : 255, Pixel(mask, new(featureX, board.Y + 4)));
            Assert.Equal(invert ? 255 : 0, Pixel(mask, board.Center));
        }
        Assert.Equal(255, Pixel(mask, new(project.IsBottom ? 58 : 42, 25)));
        Assert.Equal(255, Pixel(mask, new(project.IsBottom ? 70 : 30, 32)));
        Assert.Equal(255, Pixel(mask, new(5, 35)));
        foreach (var hole in new BlankLayoutService().MechanicalHoles(project.Blank))
            Assert.Equal(0, Pixel(mask, hole));
    }

    [Theory]
    [InlineData(ExposureMode.TopCopper, .3, 4.2, 255)]
    [InlineData(ExposureMode.TopCopper, -.3, 3.9, 0)]
    [InlineData(ExposureMode.BottomCopper, .3, 4.2, 255)]
    [InlineData(ExposureMode.BottomCopper, -.3, 3.9, 0)]
    [InlineData(ExposureMode.TopSolderMask, .3, 3.9, 255)]
    [InlineData(ExposureMode.TopSolderMask, -.3, 4.2, 0)]
    [InlineData(ExposureMode.BottomSolderMask, .3, 3.9, 255)]
    [InlineData(ExposureMode.BottomSolderMask, -.3, 4.2, 0)]
    public void CompensationKeepsItsEffectInsideBoardsAndDoesNotResizeRegistrationHoles(
        ExposureMode mode, double compensation, double featureOffsetX, int expected)
    {
        var project = Project(mode);
        project.Exposure.CopperCompensationMm = compensation;
        project.Exposure.SolderMaskCompensationMm = compensation;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);

        var board = Assert.Single(mask.Boards);
        var featureX = project.IsBottom ? board.Right - featureOffsetX : board.X + featureOffsetX;
        Assert.Equal(expected, Pixel(mask, new(featureX, board.Y + 4)));
        Assert.Equal(0, Pixel(mask, new(board.X - .2, board.Center.Y)));
        Assert.Equal(255, Pixel(mask, new(board.X - .6, board.Center.Y)));
        Assert.Equal(0, Pixel(mask, new(11.8, 10)));
        Assert.Equal(255, Pixel(mask, new(12.2, 10)));
    }

    [Theory]
    [InlineData(ExposureMode.TopCopper)]
    [InlineData(ExposureMode.BottomCopper)]
    [InlineData(ExposureMode.TopSolderMask)]
    [InlineData(ExposureMode.BottomSolderMask)]
    public void OutsideContourIsHalfAMillimeterOnBothRasterAxes(ExposureMode mode)
    {
        var project = Project(mode);
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);
        var board = Assert.Single(mask.Boards);

        // На тестовом LCD 0,5 мм соответствуют пяти столбцам и десяти строкам.
        for (var i = 1; i <= 5; i++)
            Assert.Equal(0, Pixel(mask, new(board.X - i * .1, board.Center.Y)));
        for (var i = 0; i < 5; i++)
            Assert.Equal(0, Pixel(mask, new(board.Right + i * .1, board.Center.Y)));
        for (var i = 1; i <= 10; i++)
            Assert.Equal(0, Pixel(mask, new(board.Center.X, board.Top + i * .05)));
        for (var i = 0; i < 10; i++)
            Assert.Equal(0, Pixel(mask, new(board.Center.X, board.Y - i * .05)));

        Assert.Equal(255, Pixel(mask, new(board.X - .6, board.Center.Y)));
        Assert.Equal(255, Pixel(mask, new(board.Right + .5, board.Center.Y)));
        Assert.Equal(255, Pixel(mask, new(board.Center.X, board.Top + .55)));
        Assert.Equal(255, Pixel(mask, new(board.Center.X, board.Y - .5)));
        var inside = project.CurrentTransform.Invert ? 255 : 0;
        Assert.Equal(inside, Pixel(mask, new(board.X + .1, board.Center.Y)));
        Assert.Equal(inside, Pixel(mask, new(board.Right - .1, board.Center.Y)));
        Assert.Equal(inside, Pixel(mask, new(board.Center.X, board.Y + .1)));
        Assert.Equal(inside, Pixel(mask, new(board.Center.X, board.Top - .1)));
    }

    [Theory]
    [InlineData(ExposureMode.TopCopper)]
    [InlineData(ExposureMode.BottomCopper)]
    [InlineData(ExposureMode.TopSolderMask)]
    [InlineData(ExposureMode.BottomSolderMask)]
    public void ContoursInNarrowGapsPreserveEveryBoardEdge(ExposureMode mode)
    {
        File.WriteAllText(_artwork,
            "%FSLAX45Y45*%\n%MOMM*%\n%ADD10R,20X10*%\nD10*\nX11000000Y5500000D03*\nM02*\n");
        var project = Project(mode);
        project.CurrentTransform.Invert = false;
        project.Blank.WidthMm = 100;
        project.Blank.HeightMm = 70;
        project.Panelization = new PanelizationSettings
        {
            Mode = PlacementMode.FillBlank, SpacingXmm = .2, SpacingYmm = .2,
            MarginLeftMm = 20, MarginRightMm = 20, MarginBottomMm = 20, MarginTopMm = 20
        };
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);

        Assert.Equal(4, mask.Boards.Count);
        foreach (var board in mask.Boards)
        {
            Assert.Equal(255, Pixel(mask, board.Center));
            Assert.Equal(255, Pixel(mask, new(board.X + .1, board.Center.Y)));
            Assert.Equal(255, Pixel(mask, new(board.Right - .1, board.Center.Y)));
            Assert.Equal(255, Pixel(mask, new(board.Center.X, board.Y + .1)));
            Assert.Equal(255, Pixel(mask, new(board.Center.X, board.Top - .1)));
        }
        var lowerLeft = mask.Boards.OrderBy(board => board.Y).ThenBy(board => board.X).First();
        Assert.Equal(0, Pixel(mask, new(lowerLeft.Right + .1, lowerLeft.Center.Y)));
        Assert.Equal(0, Pixel(mask, new(lowerLeft.Center.X, lowerLeft.Top + .1)));
    }

    [Theory]
    [InlineData(ExposureMode.TopCopper)]
    [InlineData(ExposureMode.BottomCopper)]
    [InlineData(ExposureMode.TopSolderMask)]
    [InlineData(ExposureMode.BottomSolderMask)]
    public void ContourAtBlankEdgeIsClippedWithoutMovingTheBoard(ExposureMode mode)
    {
        var project = Project(mode);
        project.PcbPositionMm = new(0, 30);
        project.Blank.WidthMm = 100;
        project.Blank.HeightMm = 70;
        using var mask = Service().Build(project, Printer, RasterGeometry.Native(Printer), Board);

        var board = Assert.Single(mask.Boards);
        Assert.Equal(project.IsBottom ? 80 : 0, board.X);
        Assert.Equal(0, Pixel(mask, new(-.2, board.Center.Y)));
        Assert.Equal(0, Pixel(mask, new(100.2, board.Center.Y)));
        var freeSide = project.IsBottom ? board.X - .2 : board.Right + .2;
        Assert.Equal(0, Pixel(mask, new(freeSide, board.Center.Y)));
        var fillSide = project.IsBottom ? board.X - .7 : board.Right + .7;
        Assert.Equal(255, Pixel(mask, new(fillSide, board.Center.Y)));
    }

    private ProjectModel Project(ExposureMode mode)
    {
        var project = new ProjectModel { Mode = mode, PcbPositionMm = new(25, 20) };
        project.LayerPaths[project.CurrentLayerKind] = _artwork;
        return project;
    }

    private static int Pixel(MaskResult mask, PointMm point)
    {
        var pixel = new CoordinateTransformService().BlankToPixel(point, mask.BlankOnLcd, RasterGeometry.Native(Printer));
        return Marshal.ReadByte(mask.Image.DataPointer, pixel.Y * mask.Image.Step + pixel.X);
    }

    private static ExposureRasterService Service()
    {
        var layout = new BlankLayoutService();
        return new(new CoordinateTransformService(), layout, new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService());
    }

    public void Dispose() => File.Delete(_artwork);
}
