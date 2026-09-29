using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PcbExpo.Core;

public sealed record ExposureCalibrationExportResult(string Directory, IReadOnlyList<RoundTripResult> Files);

public sealed class ExposureCalibrationExportService
{
    public ExposureCalibrationExportResult Export(ProjectModel project, PrinterInfo printer, string parentDirectory,
        IProgress<string>? progress = null)
    {
        // Capture settings before creating files; production profiles and the active preview stay unchanged.
        var snapshot = JsonSerializer.Deserialize<ProjectModel>(JsonSerializer.Serialize(project, LocalStorage.JsonOptions), LocalStorage.JsonOptions)!;
        snapshot.Mode = ExposureMode.ExposureCalibration;
        snapshot.ProcessCalibration.Validate(requireTimes: true);
        var pattern = ExposureCalibrationPattern.Create(snapshot.Blank, snapshot.ProcessCalibration);
        new CoordinateTransformService().CenterBlank(printer, snapshot.Blank);
        if (!Directory.Exists(parentDirectory)) throw new DirectoryNotFoundException("Выберите существующую папку экспорта.");
        var directory = Path.Combine(parentDirectory, $"exposure-calibration_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(directory);
        var coordinates = new CoordinateTransformService();
        var layout = new BlankLayoutService();
        var raster = new ExposureRasterService(coordinates, layout, new PanelizationService(layout),
            new GerberRenderService(), new ExposureMaskService());
        var files = new List<RoundTripResult>();
        File.WriteAllText(Path.Combine(directory, "settings.pcbexpo.json"), JsonSerializer.Serialize(snapshot, LocalStorage.JsonOptions));
        var table = new StringBuilder("file\ttime_s\tcell\tline_mm\tgap_mm\tcompensation_mm\tcompensation_X_mm\tcompensation_Y_mm\tH_lines_ok\tH_gaps_open\tV_lines_ok\tV_gaps_open\n");
        var times = snapshot.ProcessCalibration.TimesSeconds.Order().ToArray();
        for (var index = 0; index < times.Length; index++)
        {
            var time = times[index];
            var name = $"{index + 1:00}_T{Number(time).Replace('.', 'p')}s.cxdlpv4";
            foreach (var cell in pattern.Cells)
                table.AppendLine(string.Join('\t', name, Number(time), cell.Id, Number(cell.LineWidthMm), Number(cell.GapMm),
                    Number(cell.CompensationMm), Effective(cell.CompensationMm, printer.PixelPitchXmm),
                    Effective(cell.CompensationMm, printer.PixelPitchYmm), "", "", "", ""));
        }
        File.WriteAllText(Path.Combine(directory, "results.tsv"), table.ToString(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "README.txt"), Instructions, Encoding.UTF8);
        try
        {
            for (var index = 0; index < times.Length; index++)
            {
                var time = times[index];
                progress?.Report($"Экспорт калибровки {index + 1}/{times.Length}: {Number(time)} с. Папка: {directory}");
                snapshot.Exposure.ProcessCalibrationSeconds = time;
                using var mask = raster.Build(snapshot, printer, RasterGeometry.Native(printer), null);
                files.Add(new Cxdlpv4TemplateService().ExportAndVerify(snapshot.TemplatePath,
                    Path.Combine(directory, $"{index + 1:00}_T{Number(time).Replace('.', 'p')}s.cxdlpv4"),
                    mask.Image, time, snapshot.Exposure.LightPwm));
            }
            return new ExposureCalibrationExportResult(directory, files);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Серия не завершена ({files.Count}/{times.Length} файлов проверено). Результаты сохранены в {directory}. {error.Message}", error);
        }
    }

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Effective(double compensation, double pitch) => Number(Math.Sign(compensation) *
        Math.Round(Math.Abs(compensation) / pitch, MidpointRounding.AwayFromZero) * pitch);

    public const string Instructions = """
        КАЛИБРОВКА ВРЕМЕНИ И КОМПЕНСАЦИИ ЭКСПОЗИЦИИ
        Каждый CXDLPV4 содержит один слой и одно время, указанное в имени и на рисунке.
        При записи CXDLPV4 слой и миниатюры зеркалируются по X для совпадения узора на плате с preview приложения.
        Каждый файл экспонируйте ОДИН раз на СВЕЖЕМ образце. Повторные экспозиции одного образца складываются.
        Материал, ламинация/толщина, контакт с LCD, PWM и проявление должны быть одинаковыми во всех пробах.
        R — строка; под R указаны исходные толщина линии / минимальный зазор в мм.
        C — столбец; рядом указана компенсация в мм. В каждой ячейке две группы по шесть зигзагов: H и V.
        Плюс расширяет белую область, минус сужает её. При инверсии белый фон расширяется/сужается вместо линий.
        Компенсация задаётся как смещение края, а не изменение полной ширины: у прямой линии эффект примерно удвоен.
        Реальное смещение квантовано отдельно по шагу LCD X/Y; точные номинальные сдвиги указаны в results.tsv.
        После проявления проверьте непрерывность линий и отсутствие перемычек в просветах, особенно на поворотах.
        Найдите строку с толщиной/зазором вашей платы. Обе группы H/V должны пройти проверку.
        Сначала ищите подходящее время при компенсации 0, затем уточняйте компенсацию и повторяйте сравнение времён.
        Не выбирайте пробу с открытыми зазорами, если тонкие линии исчезли или стали рваными.
        Если используется травление, финальный выбор подтвердите после травления: оно тоже меняет ширину и зазоры.
        Если несколько проб проходят, предпочтите компенсацию ближе к нулю и повторите опыт для подтверждения.
        results.tsv — таблица всех ячеек и пустые поля для записи результатов H/V.
        settings.pcbexpo.json — параметры матрицы, список времён и полярность этого опыта.
        Результат выбирается по реальному образцу, а не по preview. Автоматической оценки фотографии нет.
        В приложении: «Настроить калибровку» → укажите время удачной пробы и компенсацию её столбца → примените к меди или паяльной маске.
        """;
}
