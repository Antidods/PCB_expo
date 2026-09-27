using Emgu.CV;
using PcbExpo.Core;

var template = args.Length > 0 && File.Exists(args[0]) ? args[0] : Path.Combine(Environment.CurrentDirectory, "150x100.cxdlpv4");
var info = new Cxdlpv4TemplateService().ReadInfo(template);
Console.WriteLine($"{info.Format}: {info.ResolutionX} × {info.ResolutionY} px");
Console.WriteLine($"LCD: {info.DisplayWidthMm} × {info.DisplayHeightMm} mm");
Console.WriteLine($"Pixel pitch: X={info.PixelPitchXmm:F6} mm, Y={info.PixelPitchYmm:F6} mm");
Console.WriteLine($"Template: {info.TemplateLayerCount} layers, {info.TemplateExposureSeconds} s, PWM {info.TemplateLightPwm}");
if (args.Length > 1)
{
    using var package = new GerberImportService().Import(args[1]);
    Console.WriteLine($"Board: {package.BoardBoundsMm} mm; fallback={package.OutlineFallback}");
    foreach (var layer in package.Layers) Console.WriteLine($"{layer.Kind}: {layer.Name}");
    if (args.Length > 2)
    {
        var project = new ProjectModel { TemplatePath = template, GerberSourcePath = args[1] };
        project.LayerPaths = package.Layers.Where(x => x.Kind is GerberLayerKind.TopCopper or GerberLayerKind.BottomCopper or GerberLayerKind.TopSolderMask or GerberLayerKind.BottomSolderMask)
            .ToDictionary(x => x.Kind, x => x.Path);
        var coord = new CoordinateTransformService();
        var layout = new BlankLayoutService();
        var rasterService = new ExposureRasterService(coord, layout,
            new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService());
        using var result = rasterService.Build(project, info, RasterGeometry.Preview(info), package.BoardBoundsMm);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        CvInvoke.Imwrite(args[2], result.Image);
        Console.WriteLine($"Preview: {args[2]}");
        if (args.Length > 3)
        {
            using var native = rasterService.Build(project, info, RasterGeometry.Native(info), package.BoardBoundsMm);
            var check = new Cxdlpv4TemplateService().ExportAndVerify(template, args[3], native.Image, 1.23);
            Console.WriteLine($"Round-trip: {check.LayerCount} layer, {check.ExposureSeconds} s, different pixels {check.DifferentPixels}");
        }
    }
}

