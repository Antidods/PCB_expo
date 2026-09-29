namespace PcbExpo.Core;

public sealed record CalibrationPattern(PointMm LineStart, PointMm LineEnd, RectMm Square)
{
    public const double MinimumWidthMm = 110;
    public const double MinimumHeightMm = 70;
    public const double LineLengthMm = 100;
    public const double SquareSizeMm = 50;

    public static CalibrationPattern Create(BlankProfile blank)
    {
        if (!double.IsFinite(blank.WidthMm) || !double.IsFinite(blank.HeightMm) ||
            blank.WidthMm < MinimumWidthMm || blank.HeightMm < MinimumHeightMm)
            throw new InvalidOperationException("Для калибровочного рисунка нужна заготовка не меньше 110 × 70 мм.");
        var center = new PointMm(blank.WidthMm / 2, blank.HeightMm / 2);
        return new CalibrationPattern(new(center.X - 50, center.Y + 28),
            new(center.X + 50, center.Y + 28), new(center.X - 25, center.Y - 26, SquareSizeMm, SquareSizeMm));
    }
}
