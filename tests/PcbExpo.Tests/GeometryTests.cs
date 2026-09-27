using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class GeometryTests
{
    private static readonly PrinterInfo Printer = new("test", "CXDLPV4", 1000, 500, 200, 100, 0.2, 0.2, 1, 1, 255);
    private static readonly BlankProfile Blank = new()
    {
        WidthMm = 100, HeightMm = 50, HoleInsetXmm = 10, HoleInsetYmm = 10,
        AlignmentInsetXmm = 8, AlignmentInsetYmm = 7
    };

    [Fact]
    public void BlankIsCentered() => Assert.Equal(new RectMm(50, 25, 100, 50),
        new CoordinateTransformService().CenterBlank(Printer, Blank));

    [Fact]
    public void OversizedBlankIsRejected() => Assert.Throws<InvalidOperationException>(() =>
        new CoordinateTransformService().CenterBlank(Printer, new BlankProfile { WidthMm = 201, HeightMm = 50 }));

    [Fact]
    public void MillimetersConvertToRealPixelPitch()
    {
        var raster = RasterGeometry.Native(Printer);
        var pixel = new CoordinateTransformService().LcdToPixel(new PointMm(20, 10), raster);
        Assert.Equal(new PixelPoint(100, 450), pixel);
    }

    [Fact]
    public void PcbPositionIsRelativeToBlank()
    {
        var coordinates = new CoordinateTransformService();
        var lcd = coordinates.BlankToLcd(new RectMm(10, 5, 30, 15), coordinates.CenterBlank(Printer, Blank));
        Assert.Equal(new RectMm(60, 30, 30, 15), lcd);
    }

    [Fact]
    public void BottomFlipUsesBlankCenterForOffCenterBoard()
    {
        var flipped = new CoordinateTransformService().FlipBlankAroundVerticalAxis(new RectMm(10, 5, 30, 15), Blank);
        Assert.Equal(new RectMm(60, 5, 30, 15), flipped);
        Assert.NotEqual(10, flipped.X);
    }

    [Fact]
    public void RegistrationHasCenterAndFourOuterPoints()
    {
        var points = new BlankLayoutService().AlignmentPoints(Blank);
        Assert.Equal(5, points.Count);
        Assert.Equal(new PointMm(50, 25), points[0]);
        Assert.Contains(new PointMm(8, 43), points);
        Assert.Contains(new PointMm(92, 7), points);
    }

    [Fact]
    public void MechanicalHolesAreSymmetric()
    {
        var holes = new BlankLayoutService().MechanicalHoles(Blank);
        Assert.Equal(4, holes.Count);
        Assert.Contains(new PointMm(10, 10), holes);
        Assert.Contains(new PointMm(90, 40), holes);
        Assert.Equal(4, Blank.RegistrationHoleDiameterMm);
    }

    [Fact]
    public void FillBlankCountsWholeBoardsWithMargins()
    {
        var settings = new PanelizationSettings
        {
            Mode = PlacementMode.FillBlank, SpacingXmm = 5, SpacingYmm = 5,
            MarginLeftMm = 20, MarginRightMm = 20, MarginBottomMm = 18, MarginTopMm = 18
        };
        var boards = new PanelizationService(new BlankLayoutService()).Layout(20, 10, Blank, new PointMm(0, 0), settings);
        Assert.Equal(2, boards.Count);
    }

    [Fact]
    public void HoleExclusionRemovesIntersectingBoards()
    {
        var settings = new PanelizationSettings { Mode = PlacementMode.FillBlank, SpacingXmm = 0, SpacingYmm = 0 };
        var boards = new PanelizationService(new BlankLayoutService()).Layout(20, 10, Blank, new PointMm(0, 0), settings);
        Assert.Equal(17, boards.Count);
        Assert.DoesNotContain(boards, b => new BlankLayoutService().IntersectsHoleExclusion(b, Blank));
    }

    [Fact]
    public void CompensationDilatesAndErodes()
    {
        using var positive = Dot();
        using var negative = new Mat(11, 11, DepthType.Cv8U, 1);
        negative.SetTo(new MCvScalar(255));
        var beforePositive = CvInvoke.CountNonZero(positive);
        var beforeNegative = CvInvoke.CountNonZero(negative);
        var service = new ExposureMaskService();
        var raster = new RasterGeometry(11, 11, 11, 11);
        service.Apply(positive, false, false, false, 1, raster);
        service.Apply(negative, false, false, false, -1, raster);
        Assert.True(CvInvoke.CountNonZero(positive) > beforePositive);
        Assert.True(CvInvoke.CountNonZero(negative) < beforeNegative);
    }

    [Fact]
    public void MirrorAndInvertWorkOnAsymmetricFigure()
    {
        using var mat = Dot();
        var service = new ExposureMaskService();
        var raster = new RasterGeometry(11, 11, 11, 11);
        service.Apply(mat, false, true, false, 0, raster);
        Assert.Equal(new Rectangle(8, 2, 1, 1), CvInvoke.BoundingRectangle(mat));
        service.Apply(mat, true, false, false, 0, raster);
        Assert.Equal(120, CvInvoke.CountNonZero(mat));
    }

    private static Mat Dot()
    {
        var mat = new Mat(11, 11, DepthType.Cv8U, 1);
        mat.SetTo(new MCvScalar(0));
        CvInvoke.Circle(mat, new Point(2, 2), 0, new MCvScalar(255), -1);
        return mat;
    }
}
