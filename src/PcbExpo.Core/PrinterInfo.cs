using UVtools.Core.FileFormats;

namespace PcbExpo.Core;

public sealed record PrinterInfo(
    string TemplatePath,
    string Format,
    uint ResolutionX,
    uint ResolutionY,
    double DisplayWidthMm,
    double DisplayHeightMm,
    double PixelPitchXmm,
    double PixelPitchYmm,
    uint TemplateLayerCount,
    float TemplateExposureSeconds,
    byte TemplateLightPwm);

public sealed class Cxdlpv4TemplateService
{
    public PrinterInfo ReadInfo(string templatePath)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Не найден шаблон CXDLPV4.", templatePath);

        using var file = FileFormat.Open(templatePath)
            ?? throw new InvalidDataException("UVtools не смог открыть шаблон.");
        if (file is not CrealityCXDLPv4File)
            throw new InvalidDataException("Шаблон не распознан как Creality CXDLPV4.");
        if (file.ResolutionX == 0 || file.ResolutionY == 0 ||
            file.DisplayWidth <= 0 || file.DisplayHeight <= 0)
            throw new InvalidDataException("В шаблоне отсутствуют параметры LCD.");

        return new PrinterInfo(
            Path.GetFullPath(templatePath),
            nameof(CrealityCXDLPv4File),
            file.ResolutionX,
            file.ResolutionY,
            file.DisplayWidth,
            file.DisplayHeight,
            file.DisplayWidth / file.ResolutionX,
            file.DisplayHeight / file.ResolutionY,
            file.LayerCount,
            file.ExposureTime,
            file.LightPWM);
    }
}
