namespace PcbExpo.Core;

public sealed class ExposureCalibrationSettings
{
    public List<double> LineWidthsMm { get; set; } = [0.10, 0.15, 0.20];
    public List<double> GapsMm { get; set; } = [0.05, 0.10, 0.15, 0.20];
    public List<double> CompensationsMm { get; set; } = [-0.05, -0.025, 0, 0.025, 0.05];
    public List<double> TimesSeconds { get; set; } = [];
    public double SelectedCompensationMm { get; set; }

    public void Validate(bool requireTimes = false)
    {
        Check(LineWidthsMm, 1, 6, v => v >= 0.02 && v <= 1, "Толщины линий: от 0,02 до 1 мм, не больше 6 значений.");
        Check(GapsMm, 1, 6, v => v >= 0.02 && v <= 1, "Зазоры: от 0,02 до 1 мм, не больше 6 значений.");
        Check(CompensationsMm, 1, 7, v => Math.Abs(v) <= 0.5, "Компенсация: от −0,5 до +0,5 мм, не больше 7 значений.");
        Check(TimesSeconds, requireTimes ? 1 : 0, 12, v => v > 0 && v <= 3600,
            "Задайте до 12 разных времён экспозиции от 0 до 3600 с (ноль не допускается).");
        if (!double.IsFinite(SelectedCompensationMm)) throw new InvalidOperationException("Некорректная компенсация результата.");
    }

    private static void Check(List<double>? values, int minimum, int maximum, Func<double, bool> valid, string message)
    {
        if (values is null || values.Count < minimum || values.Count > maximum ||
            values.Any(v => !double.IsFinite(v) || !valid(v)) || values.Distinct().Count() != values.Count)
            throw new InvalidOperationException(message);
    }
}

public sealed record ExposureCalibrationCell(int Row, int Column, double LineWidthMm, double GapMm,
    double CompensationMm, RectMm Bounds, double PlotSizeMm)
{
    public string Id => $"R{Row + 1:00}C{Column + 1:00}";
    public const int LineCount = 6;
    public IReadOnlyList<PointMm[]> Paths(bool vertical)
    {
        var pitch = (LineWidthMm + GapMm) * Math.Sqrt(2);
        var spread = (LineCount - 1) * pitch + 0.4;
        var result = new List<PointMm[]>();
        for (var line = 0; line < LineCount; line++)
        {
            var points = new PointMm[9];
            for (var i = 0; i < points.Length; i++)
            {
                var y = (PlotSizeMm - spread) / 2 + line * pitch + (i % 2 == 0 ? 0 : 0.4);
                // Four teeth with 45-degree legs; larger matrices keep the same physical test length.
                var x = (PlotSizeMm - 3.2) / 2 + i * 0.4;
                points[i] = vertical ? new PointMm(Bounds.X + PlotSizeMm + 1 + y, Bounds.Y + x)
                    : new PointMm(Bounds.X + x, Bounds.Y + y);
            }
            result.Add(points);
        }
        return result;
    }
}

public sealed record ExposureCalibrationPattern(RectMm Bounds, IReadOnlyList<ExposureCalibrationCell> Cells,
    IReadOnlyList<(double Width, double Gap)> Rows)
{
    public static ExposureCalibrationPattern Create(BlankProfile blank, ExposureCalibrationSettings settings)
    {
        settings.Validate();
        var rows = settings.LineWidthsMm.SelectMany(w => settings.GapsMm.Select(g => (Width: w, Gap: g))).ToArray();
        var plot = Math.Max(4.5, (ExposureCalibrationCell.LineCount - 1) * Math.Sqrt(2) *
            (settings.LineWidthsMm.Max() + settings.GapsMm.Max()) + 0.4 + settings.LineWidthsMm.Max() +
            2 * settings.CompensationsMm.Max(Math.Abs) + 1);
        var columnWidth = plot * 2 + 2;
        var rowHeight = plot + 1.5;
        var width = 23 + columnWidth * settings.CompensationsMm.Count;
        var height = 16 + rowHeight * rows.Length;
        if (!double.IsFinite(blank.WidthMm) || !double.IsFinite(blank.HeightMm) ||
            blank.WidthMm < width || blank.HeightMm < height)
            throw new InvalidOperationException($"Матрице нужна заготовка не меньше {Math.Ceiling(width * 10) / 10:0.0} × {Math.Ceiling(height * 10) / 10:0.0} мм. Уменьшите число вариантов или увеличьте заготовку.");
        var bounds = new RectMm((blank.WidthMm - width) / 2, (blank.HeightMm - height) / 2, width, height);
        var cells = new List<ExposureCalibrationCell>();
        for (var row = 0; row < rows.Length; row++)
        for (var column = 0; column < settings.CompensationsMm.Count; column++)
            cells.Add(new ExposureCalibrationCell(row, column, rows[row].Width, rows[row].Gap, settings.CompensationsMm[column],
                new RectMm(bounds.X + 20 + column * columnWidth, bounds.Top - 12 - (row + 1) * rowHeight,
                    columnWidth - 1, plot), plot));
        return new ExposureCalibrationPattern(bounds, cells, rows);
    }
}
