using System.Buffers.Binary;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class RasterImageExportTests
{
    [Fact]
    public void PngPreservesPixelsOrientationAndIndependentPhysicalScale()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcbexpo-raster-{Guid.NewGuid():N}.png");
        try
        {
            var raster = new RasterGeometry(400, 150, 100, 60);
            using var image = new Mat(raster.HeightPx, raster.WidthPx, DepthType.Cv8U, 1);
            image.SetTo(new MCvScalar(0));
            CvInvoke.Rectangle(image, new Rectangle(13, 17, 41, 19), new MCvScalar(255), -1);
            using var before = image.Clone();
            new RasterImageExportService().ExportPng(path, image, raster);
            using var decoded = CvInvoke.Imread(path, ImreadModes.Grayscale);
            Assert.Equal(raster.WidthPx, decoded.Width);
            Assert.Equal(raster.HeightPx, decoded.Height);
            using var difference = new Mat();
            CvInvoke.AbsDiff(before, decoded, difference);
            Assert.Equal(0, CvInvoke.CountNonZero(difference));
            CvInvoke.AbsDiff(before, image, difference);
            Assert.Equal(0, CvInvoke.CountNonZero(difference));
            var png = File.ReadAllBytes(path);
            Assert.Equal("pHYs", System.Text.Encoding.ASCII.GetString(png, 37, 4));
            Assert.Equal(4000u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(41, 4)));
            Assert.Equal(2500u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(45, 4)));
            Assert.Equal(1, png[49]);
            // Эталон CRC для pHYs(4000, 2500, метр), рассчитанный независимо через zlib.
            Assert.Equal(0xa5765e66u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(50, 4)));
            Assert.Throws<IOException>(() => new RasterImageExportService().ExportPng(path, image, raster));
            Assert.Equal(png, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(99, 60)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 60)]
    public void InvalidDimensionsAreRejectedBeforeCreatingFile(double width, double height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcbexpo-raster-invalid-{Guid.NewGuid():N}.png");
        using var image = new Mat(150, 400, DepthType.Cv8U, 1);
        var raster = width == 99 ? new RasterGeometry(399, 150, width, height)
            : new RasterGeometry(400, 150, width, height);
        Assert.Throws<InvalidOperationException>(() => new RasterImageExportService().ExportPng(path, image, raster));
        Assert.False(File.Exists(path));
    }
}
