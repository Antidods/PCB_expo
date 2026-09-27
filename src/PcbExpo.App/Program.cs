using PcbExpo.Core;

var template = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "150x100.cxdlpv4");
var info = new Cxdlpv4TemplateService().ReadInfo(template);
Console.WriteLine($"{info.Format}: {info.ResolutionX} × {info.ResolutionY} px");
Console.WriteLine($"LCD: {info.DisplayWidthMm} × {info.DisplayHeightMm} mm");
Console.WriteLine($"Pixel pitch: X={info.PixelPitchXmm:F6} mm, Y={info.PixelPitchYmm:F6} mm");
Console.WriteLine($"Template: {info.TemplateLayerCount} layers, {info.TemplateExposureSeconds} s, PWM {info.TemplateLightPwm}");
