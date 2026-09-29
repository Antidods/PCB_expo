using Emgu.CV;
using PcbExpo.Core;

namespace PcbExpo.App;

internal static class DiagnosticCli
{
    public static void CheckRuntime()
    {
        // Проверяем загрузку OpenCV и PNG-кодека из опубликованного exe без исходных файлов.
        using var mask = new Mat(4, 4, Emgu.CV.CvEnum.DepthType.Cv8U, 1);
        mask.SetTo(new Emgu.CV.Structure.MCvScalar(255));
        var png = CvInvoke.Imencode(".png", mask);
        if (CvInvoke.CountNonZero(mask) != 16 || png.Length == 0)
            throw new InvalidOperationException("Проверка нативных библиотек не пройдена.");
        Console.WriteLine($"Runtime OK: .NET {Environment.Version}; OpenCV; PNG ({png.Length} bytes)");
    }

    public static void Run(string[] args)
    {
        var template = args.Length > 0 ? args[0] : ApplicationPaths.DefaultTemplate;
        var info = new Cxdlpv4TemplateService().ReadInfo(template);
        Console.WriteLine($"{info.Format}: {info.ResolutionX} × {info.ResolutionY} px");
        Console.WriteLine($"LCD: {info.DisplayWidthMm} × {info.DisplayHeightMm} mm");
        Console.WriteLine($"Pixel pitch: X={info.PixelPitchXmm:F6} mm, Y={info.PixelPitchYmm:F6} mm");
        Console.WriteLine($"Template: {info.TemplateLayerCount} layers, {info.TemplateExposureSeconds} s, PWM {info.TemplateLightPwm}");
        if (args.Length < 2) return;
        using var package = new GerberImportService().Import(args[1]);
        Console.WriteLine($"Board: {package.BoardBoundsMm} mm; fallback={package.OutlineFallback}");
        foreach (var layer in package.Layers) Console.WriteLine($"{layer.Kind}: {layer.Name}");
        if (args.Length < 3) return;
        var project = new ProjectModel { TemplatePath = template, GerberSourcePath = args[1] };
        project.LayerPaths = package.Layers.Where(x => x.Kind is GerberLayerKind.TopCopper or
            GerberLayerKind.BottomCopper or GerberLayerKind.TopSolderMask or GerberLayerKind.BottomSolderMask)
            .ToDictionary(x => x.Kind, x => x.Path);
        var coord = new CoordinateTransformService();
        var layout = new BlankLayoutService();
        var rasterService = new ExposureRasterService(coord, layout,
            new PanelizationService(layout), new GerberRenderService(), new ExposureMaskService());
        if (args[2] == "--all")
        {
            var folder = Path.Combine(Environment.CurrentDirectory, ".local", "source-check");
            Directory.CreateDirectory(folder);
            foreach (var mode in Enum.GetValues<ExposureMode>())
            {
                project.Mode = mode;
                using var rendered = rasterService.Build(project, info, RasterGeometry.Preview(info), package.BoardBoundsMm);
                var previewPath = Path.Combine(folder, $"{mode}.png");
                CvInvoke.Imwrite(previewPath, rendered.Image);
                Console.WriteLine($"{mode}: copies={rendered.Boards.Count}, lit pixels={CvInvoke.CountNonZero(rendered.Image)}, preview={previewPath}");
            }
            return;
        }
        using var result = rasterService.Build(project, info, RasterGeometry.Preview(info), package.BoardBoundsMm);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        CvInvoke.Imwrite(args[2], result.Image);
        Console.WriteLine($"Preview: {args[2]}");
        if (args.Length < 4) return;
        using var native = rasterService.Build(project, info, RasterGeometry.Native(info), package.BoardBoundsMm);
        var check = new Cxdlpv4TemplateService().ExportAndVerify(template, args[3], native.Image, 1.23);
        Console.WriteLine($"Round-trip: {check.LayerCount} layer, {check.ExposureSeconds} s, different pixels {check.DifferentPixels}");
    }
}
