using System.Buffers.Binary;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace PcbExpo.Core;

public sealed class RasterImageExportService
{
    public void ExportPng(string outputPath, Mat image, RasterGeometry raster)
    {
        if (image.Width != raster.WidthPx || image.Height != raster.HeightPx ||
            image.Depth != DepthType.Cv8U || image.NumberOfChannels != 1)
            throw new InvalidOperationException("Для PNG нужен одноканальный растр 8 бит в полном разрешении.");
        if (!double.IsFinite(raster.DisplayWidthMm) || !double.IsFinite(raster.DisplayHeightMm) ||
            raster.DisplayWidthMm <= 0 || raster.DisplayHeightMm <= 0)
            throw new InvalidOperationException("Физический размер растра должен быть положительным.");

        var png = CvInvoke.Imencode(".png", image);
        // OpenCV сохраняет пиксели без масштаба. pHYs задаёт независимую плотность X/Y в пикселях на метр.
        Span<byte> physical = stackalloc byte[21];
        BinaryPrimitives.WriteUInt32BigEndian(physical, 9);
        "pHYs"u8.CopyTo(physical[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(physical[8..], checked((uint)Math.Round(raster.PixelsPerMmX * 1000)));
        BinaryPrimitives.WriteUInt32BigEndian(physical[12..], checked((uint)Math.Round(raster.PixelsPerMmY * 1000)));
        physical[16] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(physical[17..], Crc32(physical[4..17]));
        // IHDR вместе с сигнатурой занимает 33 байта; pHYs должен предшествовать IDAT.
        using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(png.AsSpan(0, 33));
        stream.Write(physical);
        stream.Write(png.AsSpan(33));
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        }
        return ~crc;
    }
}
