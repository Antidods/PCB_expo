using System.Globalization;
using System.Runtime.CompilerServices;
using PcbExpo.Core;

[assembly: InternalsVisibleTo("PcbExpo.Tests")]

namespace PcbExpo.App;

internal sealed record DisplayChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal enum AlignmentCommand { Center, Left, Right, Top, Bottom }

internal static class UiText
{
    public static string SafeFileName(string value) => new(value.Select(c =>
        Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    public static string Exposure(ExposureMode mode) => mode switch
    {
        ExposureMode.TopCopper => "Медь Top",
        ExposureMode.BottomCopper => "Медь Bottom",
        ExposureMode.TopSolderMask => "Паяльная маска Top",
        ExposureMode.BottomSolderMask => "Паяльная маска Bottom",
        ExposureMode.TopStencil => "Паяльный трафарет Top",
        ExposureMode.BottomStencil => "Паяльный трафарет Bottom",
        ExposureMode.Registration => "Точки центровки",
        ExposureMode.Calibration => "Калибровка",
        ExposureMode.ExposureCalibration => "Калибровка времени и компенсации",
        _ => mode.ToString()
    };

    public static string Placement(PlacementMode mode) => mode switch
    {
        PlacementMode.Single => "Одна плата",
        PlacementMode.FillBlank => "Заполнить заготовку",
        _ => mode.ToString()
    };

    public static string Layer(GerberLayerKind kind) => kind switch
    {
        GerberLayerKind.TopCopper => "Медь Top",
        GerberLayerKind.BottomCopper => "Медь Bottom",
        GerberLayerKind.TopSolderMask => "Паяльная маска Top",
        GerberLayerKind.BottomSolderMask => "Паяльная маска Bottom",
        GerberLayerKind.TopPasteMask => "Паяльная паста Top (PasteMaskLayer)",
        GerberLayerKind.BottomPasteMask => "Паяльная паста Bottom (PasteMaskLayer)",
        GerberLayerKind.BoardOutline => "Контур платы",
        GerberLayerKind.Drill => "Сверловка",
        GerberLayerKind.Other => "Другой слой",
        _ => "Не распознан"
    };

    public static string Alignment(AlignmentCommand command) => command switch
    {
        AlignmentCommand.Center => "Центрировать плату",
        AlignmentCommand.Left => "По левому краю",
        AlignmentCommand.Right => "По правому краю",
        AlignmentCommand.Top => "По верхнему краю",
        AlignmentCommand.Bottom => "По нижнему краю",
        _ => command.ToString()
    };

    public static string Number(double value) => value.ToString("0.######", CultureInfo.CurrentCulture);

    public static bool TryNumber(string? text, out double value) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public static string YesNo(bool value) => value ? "да" : "нет";

    public static string Summary(PrinterInfo printer, ProjectModel project, GerberPackage? package,
        int boardCount, IReadOnlyList<RectMm> positions)
    {
        var board = package?.BoardBoundsMm;
        var pitchWarning = Math.Abs(printer.PixelPitchXmm - printer.PixelPitchYmm) /
            Math.Max(printer.PixelPitchXmm, printer.PixelPitchYmm) > 0.05
            ? "ВНИМАНИЕ: шаг пикселя X/Y различается. Проверьте масштаб калибровкой на LCD.\n"
            : "";
        var outlineWarning = package?.OutlineFallback == true
            ? "ВНИМАНИЕ: контур платы отсутствует; габариты оценены по artwork.\n"
            : "";
        return $"Формат: {printer.Format}\nРазрешение: {printer.ResolutionX} × {printer.ResolutionY} px\n" +
            $"Поле LCD: {Number(printer.DisplayWidthMm)} × {Number(printer.DisplayHeightMm)} мм\n" +
            $"Шаг пикселя: X {printer.PixelPitchXmm:F6}, Y {printer.PixelPitchYmm:F6} мм\n" +
            $"Шаблон: {printer.TemplatePath}\n{pitchWarning}{outlineWarning}" +
            $"Плата: {(board is null ? "не загружена" : $"{Number(board.Value.Width)} × {Number(board.Value.Height)} мм")}\n" +
            $"Заготовка: {Number(project.Blank.WidthMm)} × {Number(project.Blank.HeightMm)} мм\n" +
            $"Режим: {Exposure(project.Mode)}\nПлат: {boardCount}\n" +
            $"Плата X/Y: {Number(project.PcbPositionMm.X)} / {Number(project.PcbPositionMm.Y)} мм\n" +
            $"Позиции на заготовке: {string.Join("; ", positions.Select(p => $"({Number(p.X)}, {Number(p.Y)})"))}\n" +
            $"Зеркалирование X/Y: {YesNo(project.Mode != ExposureMode.ExposureCalibration && project.CurrentTransform.MirrorX)} / {YesNo(project.Mode != ExposureMode.ExposureCalibration && project.CurrentTransform.MirrorY)}\n" +
            $"Инверсия: {YesNo(project.IsStencil || project.CurrentTransform.Invert)}\n" +
            $"Экспозиция: {Number(project.CurrentExposureSeconds)} с\n" +
            (project.Mode == ExposureMode.ExposureCalibration ?
                $"Компенсации столбцов: {string.Join("; ", project.ProcessCalibration.CompensationsMm.Select(Number))} мм\n" :
                $"Компенсация: {Number(project.CurrentCompensationMm)} мм\n") +
            $"PWM: {project.Exposure.LightPwm?.ToString() ?? "из шаблона"}\n" +
            "Физический переворот Bottom: относительно вертикальной оси центра заготовки\n" +
            $"Сглаживание: {YesNo(project.Mode != ExposureMode.ExposureCalibration && project.AntiAliasing)}\n" +
            "Белое на preview означает, что LCD пропускает UV.";
    }
}
