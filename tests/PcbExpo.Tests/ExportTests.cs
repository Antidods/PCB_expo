using System.Drawing;
using System.Security.Cryptography;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PcbExpo.Core;
using UVtools.Core.FileFormats;
using UVtools.Core.Layers;

namespace PcbExpo.Tests;

public class ExportTests
{
    [Fact]
    public void ExportMirrorsLayerAndThumbnailsWithoutChangingPreviewOrTemplate()
    {
        var template = Path.Combine(Path.GetTempPath(), $"pcbexpo-mirror-template-{Guid.NewGuid():N}.cxdlpv4");
        var output = Path.Combine(Path.GetTempPath(), $"pcbexpo-mirror-output-{Guid.NewGuid():N}.cxdlpv4");
        try
        {
            using var mask = new Mat(100, 160, DepthType.Cv8U, 1);
            mask.SetTo(new MCvScalar(0));
            CvInvoke.Rectangle(mask, new Rectangle(10, 15, 24, 40), new MCvScalar(255), -1);
            CvInvoke.Rectangle(mask, new Rectangle(34, 43, 30, 12), new MCvScalar(255), -1);
            using var before = mask.Clone();
            using (var source = new CrealityCXDLPv4File
            {
                ResolutionX = 160, ResolutionY = 100, DisplayWidth = 200, DisplayHeight = 125,
                BottomLayerCount = 1, BottomExposureTime = 1, ExposureTime = 1
            })
            {
                source.Layers = [new Layer(mask, source)];
                using var thumbnail = new Mat();
                CvInvoke.CvtColor(mask, thumbnail, ColorConversion.Gray2Bgr);
                source.SetThumbnails(thumbnail);
                source.SaveAs(template);
            }
            var templateHash = SHA256.HashData(File.ReadAllBytes(template));

            var result = new Cxdlpv4TemplateService().ExportAndVerify(template, output, mask, 1.23, 180);

            using var exported = Assert.IsType<CrealityCXDLPv4File>(FileFormat.Open(output));
            using var decoded = exported.Layers[0].LayerMat;
            using var expected = new Mat();
            CvInvoke.Flip(before, expected, FlipType.Horizontal);
            using var difference = new Mat();
            CvInvoke.AbsDiff(expected, decoded, difference);
            Assert.Equal(0, CvInvoke.CountNonZero(difference));
            CvInvoke.AbsDiff(before, mask, difference);
            Assert.Equal(0, CvInvoke.CountNonZero(difference));
            Assert.Equal(templateHash, SHA256.HashData(File.ReadAllBytes(template)));
            Assert.Equal(0, result.DifferentPixels);
            Assert.Equal((uint)1, result.LayerCount);
            Assert.Equal(1.23, result.ExposureSeconds, 2);
            foreach (var thumbnail in exported.Thumbnails)
            {
                using var gray = new Mat();
                CvInvoke.CvtColor(thumbnail, gray, ColorConversion.Bgr2Gray);
                Assert.True(CvInvoke.BoundingRectangle(gray).Left > gray.Width / 2);
            }
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            if (File.Exists(template)) File.Delete(template);
        }
    }

    [Theory]
    [InlineData(ExposureMode.Registration)]
    [InlineData(ExposureMode.TopStencil)]
    public void MaskRoundTripsThroughRealTemplate(ExposureMode mode)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PcbExpo.slnx")))
            directory = directory.Parent;
        if (directory is null) return;
        var template = Path.Combine(directory.FullName, "150x100.cxdlpv4");
        if (!File.Exists(template)) return;
        var output = Path.Combine(Path.GetTempPath(), $"pcbexpo-registration-test-{Guid.NewGuid():N}.cxdlpv4");
        try
        {
            var service = new Cxdlpv4TemplateService();
            var info = service.ReadInfo(template);
            var coordinates = new CoordinateTransformService();
            var layout = new BlankLayoutService();
            var raster = new ExposureRasterService(coordinates, layout, new PanelizationService(layout),
                new GerberRenderService(), new ExposureMaskService());
            var project = new ProjectModel { Mode = mode };
            using var package = mode == ExposureMode.TopStencil
                ? new GerberImportService().Import(Path.Combine(directory.FullName, "Gerber_Cube_PCB_Cube_2026-09-23")) : null;
            if (package is not null)
                project.LayerPaths[GerberLayerKind.TopPasteMask] = package.GetLayer(GerberLayerKind.TopPasteMask)!.Path;
            using var result = raster.Build(project, info, RasterGeometry.Native(info), package?.BoardBoundsMm);
            var check = service.ExportAndVerify(template, output, result.Image, 1.23);
            Assert.Equal((uint)1, check.LayerCount);
            Assert.Equal(0, check.DifferentPixels);
            Assert.Equal(info.ResolutionX, check.ResolutionX);
            Assert.Equal(info.ResolutionY, check.ResolutionY);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
