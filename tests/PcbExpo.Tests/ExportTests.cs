using PcbExpo.Core;

namespace PcbExpo.Tests;

public class ExportTests
{
    [Fact]
    public void RegistrationMaskRoundTripsThroughRealTemplate()
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
            var project = new ProjectModel { Mode = ExposureMode.Registration };
            using var result = raster.Build(project, info, RasterGeometry.Native(info), null);
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
