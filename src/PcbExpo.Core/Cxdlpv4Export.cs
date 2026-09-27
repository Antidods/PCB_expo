using System.Drawing;
using System.Security.Cryptography;
using Emgu.CV;
using Emgu.CV.CvEnum;
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
        var templateHash = SHA256.HashData(File.ReadAllBytes(templatePath));
        using var originalMetadata = FileFormat.Open(templatePath, FileFormat.FileDecodeType.Partial)
            as CrealityCXDLPv4File ?? throw new InvalidDataException("Не удалось прочитать метаданные шаблона.");
        var sourceVolume = originalMetadata.PrintParametersSettings.VolumeMl;
        var sourceWeight = originalMetadata.PrintParametersSettings.WeightG;
        var sourceCost = originalMetadata.PrintParametersSettings.CostDollars;
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
        // Финальный слой бинарный, поэтому уровень AA формата равен 1.
        source.HeaderSettings.AntiAliasLevel = 1;
        source.SlicerInfoSettings.AntiAliasLevel = 1;
        if (lightPwm is not null)
        {
            source.BottomLightPWM = lightPwm.Value;
            source.LightPWM = lightPwm.Value;
            source.Layers[0].LightPWM = lightPwm.Value;
        }
        using var thumbnailGray = new Mat();
        using var thumbnailBgr = new Mat();
        CvInvoke.Resize(mask, thumbnailGray, new Size(300, 170), interpolation: Inter.Nearest);
        CvInvoke.CvtColor(thumbnailGray, thumbnailBgr, ColorConversion.Gray2Bgr);
        source.SetThumbnails(thumbnailBgr);
        source.PrintParametersSettings.VolumeMl = sourceVolume;
        source.PrintParametersSettings.WeightG = sourceWeight;
        source.PrintParametersSettings.CostDollars = sourceCost;
        source.SaveAs(outputPath);
        // Полный decode/encode UVtools пересчитывает поля материала. Возвращаем исходные
        // значения частичным сохранением только метаданных уже созданного файла.
        using (var metadata = FileFormat.Open(outputPath, FileFormat.FileDecodeType.Partial)
               as CrealityCXDLPv4File ?? throw new InvalidDataException("Не удалось проверить метаданные экспорта."))
        {
            metadata.PrintParametersSettings.VolumeMl = sourceVolume;
            metadata.PrintParametersSettings.WeightG = sourceWeight;
            metadata.PrintParametersSettings.CostDollars = sourceCost;
            metadata.SaveAs(outputPath);
        }
        if (!templateHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(templatePath))))
            throw new InvalidDataException("Исходный шаблон изменился во время экспорта.");

        using (var metadata = FileFormat.Open(outputPath, FileFormat.FileDecodeType.Partial)
               as CrealityCXDLPv4File ?? throw new InvalidDataException("Не удалось повторно прочитать метаданные."))
        {
            if (metadata.PrintParametersSettings.VolumeMl != sourceVolume ||
                metadata.PrintParametersSettings.WeightG != sourceWeight ||
                metadata.PrintParametersSettings.CostDollars != sourceCost)
                throw new InvalidDataException("Поля материала шаблона изменились при экспорте.");
        }

        using var decoded = FileFormat.Open(outputPath) as CrealityCXDLPv4File
            ?? throw new InvalidDataException("Экспортированный CXDLPV4 не открывается через UVtools.");
        if (decoded.ResolutionX != sourceResolutionX || decoded.ResolutionY != sourceResolutionY ||
            Math.Abs(decoded.DisplayWidth - sourceWidth) > 0.001 ||
            Math.Abs(decoded.DisplayHeight - sourceHeight) > 0.001 || decoded.LayerCount != 1 ||
            Math.Abs(decoded.BottomExposureTime - exposureSeconds) > 0.02 ||
            Math.Abs(decoded.Layers[0].ExposureTime - exposureSeconds) > 0.02 ||
            decoded.HeaderSettings.AntiAliasLevel != 1 ||
            (lightPwm is not null && (decoded.LightPWM != lightPwm || decoded.BottomLightPWM != lightPwm)))
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
