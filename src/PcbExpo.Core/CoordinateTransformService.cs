namespace PcbExpo.Core;

public readonly record struct PointMm(double X, double Y);
public readonly record struct RectMm(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Top => Y + Height;
    public PointMm Center => new(X + Width / 2, Y + Height / 2);
}

public readonly record struct PixelPoint(int X, int Y);

public sealed class CoordinateTransformService
{
    public RectMm CenterBlank(PrinterInfo printer, BlankProfile blank)
    {
        if (blank.WidthMm <= 0 || blank.HeightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(blank), "Размер заготовки должен быть положительным.");
        if (blank.WidthMm > printer.DisplayWidthMm || blank.HeightMm > printer.DisplayHeightMm)
            throw new InvalidOperationException("Заготовка не помещается на LCD.");
        return new RectMm((printer.DisplayWidthMm - blank.WidthMm) / 2,
            (printer.DisplayHeightMm - blank.HeightMm) / 2, blank.WidthMm, blank.HeightMm);
    }

    public PointMm BlankToLcd(PointMm local, RectMm blankOnLcd) =>
        new(blankOnLcd.X + local.X, blankOnLcd.Y + local.Y);

    public RectMm BlankToLcd(RectMm local, RectMm blankOnLcd) =>
        new(blankOnLcd.X + local.X, blankOnLcd.Y + local.Y, local.Width, local.Height);

    public PixelPoint LcdToPixel(PointMm lcdPoint, RasterGeometry raster) =>
        new((int)Math.Round(lcdPoint.X * raster.PixelsPerMmX, MidpointRounding.AwayFromZero),
            (int)Math.Round((raster.DisplayHeightMm - lcdPoint.Y) * raster.PixelsPerMmY, MidpointRounding.AwayFromZero));

    public PixelPoint BlankToPixel(PointMm blankPoint, RectMm blankOnLcd, RasterGeometry raster) =>
        LcdToPixel(BlankToLcd(blankPoint, blankOnLcd), raster);

    public RectMm FlipBlankAroundVerticalAxis(RectMm board, BlankProfile blank) =>
        new(blank.WidthMm - board.Right, board.Y, board.Width, board.Height);

    public PointMm FlipBlankAroundVerticalAxis(PointMm point, BlankProfile blank) =>
        new(blank.WidthMm - point.X, point.Y);

    public static int MmToPx(double millimeters, double pixelsPerMm) =>
        (int)Math.Round(millimeters * pixelsPerMm, MidpointRounding.AwayFromZero);
}

public sealed record RasterGeometry(int WidthPx, int HeightPx, double DisplayWidthMm, double DisplayHeightMm)
{
    public double PixelsPerMmX => WidthPx / DisplayWidthMm;
    public double PixelsPerMmY => HeightPx / DisplayHeightMm;

    public static RasterGeometry Native(PrinterInfo printer) =>
        new(checked((int)printer.ResolutionX), checked((int)printer.ResolutionY),
            printer.DisplayWidthMm, printer.DisplayHeightMm);

    public static RasterGeometry Preview(PrinterInfo printer, int maxWidth = 1200, int maxHeight = 750)
    {
        var density = Math.Min(maxWidth / printer.DisplayWidthMm, maxHeight / printer.DisplayHeightMm);
        return new RasterGeometry((int)Math.Round(printer.DisplayWidthMm * density),
            (int)Math.Round(printer.DisplayHeightMm * density), printer.DisplayWidthMm, printer.DisplayHeightMm);
    }
}
