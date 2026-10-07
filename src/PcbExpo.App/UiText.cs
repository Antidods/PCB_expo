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
    public const string OutlineWarning = "ВНИМАНИЕ: контур платы отсутствует; габариты оценены по artwork.";
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
        var parts = SummaryParts(printer, project, package, boardCount, positions);
        string Fields(SummaryGroup group) => string.Concat(group.Fields.Select(f => $"{f.Label}: {f.Value}\n"));
        return Fields(parts.Groups[0]) + string.Concat(parts.Warnings.Select(w => w + "\n")) +
            string.Concat(parts.Groups.Skip(1).Select(Fields)) + parts.Footnote;
    }

    public static ExportSummary SummaryParts(PrinterInfo printer, ProjectModel project, GerberPackage? package,
        int boardCount, IReadOnlyList<RectMm> positions)
    {
        var board = package?.BoardBoundsMm;
        var warnings = new List<string>();
        if (Math.Abs(printer.PixelPitchXmm - printer.PixelPitchYmm) /
            Math.Max(printer.PixelPitchXmm, printer.PixelPitchYmm) > 0.05)
            warnings.Add("ВНИМАНИЕ: шаг пикселя X/Y различается. Проверьте масштаб калибровкой на LCD.");
        if (package?.OutlineFallback == true)
            warnings.Add(OutlineWarning);
        return new ExportSummary(warnings,
        [
            new("Принтер и шаблон",
            [
                new("Формат", printer.Format),
                new("Разрешение", $"{printer.ResolutionX} × {printer.ResolutionY} px"),
                new("Поле LCD", $"{Number(printer.DisplayWidthMm)} × {Number(printer.DisplayHeightMm)} мм"),
                new("Шаг пикселя", $"X {printer.PixelPitchXmm:F6}, Y {printer.PixelPitchYmm:F6} мм"),
                new("Шаблон", printer.TemplatePath)
            ]),
            new("Плата и заготовка",
            [
                new("Плата", board is null ? "не загружена" : $"{Number(board.Value.Width)} × {Number(board.Value.Height)} мм"),
                new("Заготовка", $"{Number(project.Blank.WidthMm)} × {Number(project.Blank.HeightMm)} мм"),
                new("Режим", Exposure(project.Mode)),
                new("Плат", boardCount.ToString()),
                new("Плата X/Y", $"{Number(project.PcbPositionMm.X)} / {Number(project.PcbPositionMm.Y)} мм"),
                new("Позиции на заготовке", string.Join("; ", positions.Select(p => $"({Number(p.X)}, {Number(p.Y)})")))
            ]),
            new("Экспозиция",
            [
                new("Зеркалирование X/Y", $"{YesNo(project.Mode != ExposureMode.ExposureCalibration && project.CurrentTransform.MirrorX)} / {YesNo(project.Mode != ExposureMode.ExposureCalibration && project.CurrentTransform.MirrorY)}"),
                new("Инверсия", YesNo(project.IsStencil || project.CurrentTransform.Invert)),
                new("Экспозиция", $"{Number(project.CurrentExposureSeconds)} с"),
                project.Mode == ExposureMode.ExposureCalibration
                    ? new("Компенсации столбцов", $"{string.Join("; ", project.ProcessCalibration.CompensationsMm.Select(Number))} мм")
                    : new("Компенсация", $"{Number(project.CurrentCompensationMm)} мм"),
                new("PWM", project.Exposure.LightPwm?.ToString() ?? "из шаблона"),
                new("Физический переворот Bottom", "относительно вертикальной оси центра заготовки"),
                new("Сглаживание", YesNo(project.Mode != ExposureMode.ExposureCalibration && project.AntiAliasing))
            ])
        ], "Белое на preview означает, что LCD пропускает UV.");
    }
}

internal sealed record SummaryField(string Label, string Value);
internal sealed record SummaryGroup(string Title, IReadOnlyList<SummaryField> Fields);
internal sealed record ExportSummary(IReadOnlyList<string> Warnings, IReadOnlyList<SummaryGroup> Groups, string Footnote);
