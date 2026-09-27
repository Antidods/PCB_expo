using Emgu.CV;
using UVtools.Core.FileFormats;
using UVtools.Core.Layers;

namespace PcbExpo.Core;

public sealed record RoundTripResult(string Path, uint ResolutionX, uint ResolutionY,
    double DisplayWidthMm, double DisplayHeightMm, uint LayerCount,
    float ExposureSeconds, int DifferentPixels);

public sealed partial class Cxdlpv4TemplateService
{
    public RoundTripResult ExportAndVerify(string templatePath, string outputPath, Mat mask,
        double exposureSeconds, byte? lightPwm = null)
    {
        if (exposureSeconds <= 0 || !double.IsFinite(exposureSeconds))
            throw new InvalidOperationException("Укажите положительное время экспозиции.");
        if (Path.GetFullPath(templatePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Нельзя перезаписывать исходный шаблон.");
        if (File.Exists(outputPath))
            throw new IOException("Выходной файл уже существует; выберите другое имя.");
        using var source = FileFormat.Open(templatePath) as CrealityCXDLPv4File
            ?? throw new InvalidDataException("Шаблон не является CXDLPV4.");
        if (mask.Width != source.ResolutionX || mask.Height != source.ResolutionY)
            throw new InvalidOperationException("Разрешение маски не соответствует шаблону.");
        var sourceWidth = source.DisplayWidth;
        var sourceHeight = source.DisplayHeight;
        var sourceResolutionX = source.ResolutionX;
        var sourceResolutionY = source.ResolutionY;

        // Один слой исключает повторную экспозицию 40 слоёв исходного печатного задания.
        source.Layers = [new Layer(mask, source)];
        source.BottomLayerCount = 1;
        source.TransitionLayerCount = 0;
        source.BottomExposureTime = (float)exposureSeconds;
        source.ExposureTime = (float)exposureSeconds;
        source.Layers[0].ExposureTime = (float)exposureSeconds;
        if (lightPwm is not null)
        {
            source.BottomLightPWM = lightPwm.Value;
            source.LightPWM = lightPwm.Value;
            source.Layers[0].LightPWM = lightPwm.Value;
        }
        source.SaveAs(outputPath);

        using var decoded = FileFormat.Open(outputPath) as CrealityCXDLPv4File
            ?? throw new InvalidDataException("Экспортированный CXDLPV4 не открывается через UVtools.");
        if (decoded.ResolutionX != sourceResolutionX || decoded.ResolutionY != sourceResolutionY ||
            Math.Abs(decoded.DisplayWidth - sourceWidth) > 0.001 ||
            Math.Abs(decoded.DisplayHeight - sourceHeight) > 0.001 || decoded.LayerCount != 1 ||
            Math.Abs(decoded.BottomExposureTime - exposureSeconds) > 0.02)
            throw new InvalidDataException("Round-trip обнаружил изменение параметров шаблона или экспозиции.");
        using var decodedMat = decoded.Layers[0].LayerMat;
        using var diff = new Mat();
        CvInvoke.AbsDiff(mask, decodedMat, diff);
        var differentPixels = CvInvoke.CountNonZero(diff);
        if (differentPixels != 0)
            throw new InvalidDataException($"Round-trip: изображение отличается в {differentPixels} пикселях.");
        return new RoundTripResult(outputPath, decoded.ResolutionX, decoded.ResolutionY,
            decoded.DisplayWidth, decoded.DisplayHeight, decoded.LayerCount,
            decoded.BottomExposureTime, differentPixels);
    }
}
