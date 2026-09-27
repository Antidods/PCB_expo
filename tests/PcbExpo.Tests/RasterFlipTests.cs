using System.Drawing;
using Emgu.CV;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class RasterFlipTests
{
    [Fact]
    public void OffCenterAsymmetricArtworkFlipsAroundBlankVerticalAxis()
    {
        var gerber = Path.Combine(Path.GetTempPath(), $"pcbexpo-flip-{Guid.NewGuid():N}.gbr");
        File.WriteAllText(gerber, "%FSLAX45Y45*%\n%MOMM*%\n%ADD10C,2.0*%\nD10*\nX200000Y300000D03*\nM02*\n");
        try
        {
            var info = new PrinterInfo("test", "CXDLPV4", 1000, 500, 200, 100, 0.2, 0.2, 1, 1, 255);
            var project = new ProjectModel
            {
                Blank = new BlankProfile { WidthMm = 100, HeightMm = 50, HoleInsetXmm = 10, HoleInsetYmm = 10 },
                PcbPositionMm = new PointMm(25, 5),
                LayerPaths = new Dictionary<GerberLayerKind, string>
                {
                    [GerberLayerKind.TopCopper] = gerber,
                    [GerberLayerKind.BottomCopper] = gerber
                }
            };
            var coordinates = new CoordinateTransformService();
            var layout = new BlankLayoutService();
            var service = new ExposureRasterService(coordinates, layout, new PanelizationService(layout),
                new GerberRenderService(), new ExposureMaskService());
            var boardBounds = new RectangleF(0, 0, 20, 10);
            using var top = service.Build(project, info, RasterGeometry.Native(info), boardBounds);
            project.Mode = ExposureMode.BottomCopper;
            using var bottom = service.Build(project, info, RasterGeometry.Native(info), boardBounds);
            var a = CvInvoke.BoundingRectangle(top.Image);
            var b = CvInvoke.BoundingRectangle(bottom.Image);
            Assert.NotEqual(a.X, b.X);
            Assert.InRange(a.X + b.Right, 998, 1002);
            Assert.Equal(a.Y, b.Y);
            Assert.Single(top.Boards);
            Assert.Single(bottom.Boards);
            Assert.Equal(55, bottom.Boards[0].X);
        }
        finally
        {
            if (File.Exists(gerber)) File.Delete(gerber);
        }
    }
}
