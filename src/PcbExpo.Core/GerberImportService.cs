using System.Drawing;
using System.IO.Compression;
using Emgu.CV;
using Emgu.CV.CvEnum;
using UVtools.Core.Gerber;

namespace PcbExpo.Core;

public enum GerberLayerKind
{
    Unknown, TopCopper, BottomCopper, TopSolderMask, BottomSolderMask,
    BoardOutline, Drill, Other, TopPasteMask, BottomPasteMask
}

public sealed record GerberLayer(string Path, string Name, GerberLayerKind Kind, string RelativePath)
{
    public override string ToString() => Name;
}

public sealed class GerberPackage : IDisposable
{
    public string SourcePath { get; init; } = "";
    public string ProjectName { get; init; } = "";
    public List<GerberLayer> Layers { get; init; } = [];
    public RectangleF? BoardBoundsMm { get; set; }
    public bool OutlineFallback { get; set; }
    public string? TemporaryDirectory { get; init; }

    public GerberLayer? GetLayer(GerberLayerKind kind) => Layers.SingleOrDefault(x => x.Kind == kind);

    public void Dispose()
    {
        if (TemporaryDirectory is not null && Directory.Exists(TemporaryDirectory))
            Directory.Delete(TemporaryDirectory, true);
    }
}

public sealed class GerberImportService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".gbr", ".gtl", ".gbl", ".gts", ".gbs", ".gko", ".gm1", ".gml",
      ".gto", ".gbo", ".gtp", ".gbp", ".gdl", ".drl", ".xln" };

    public GerberPackage Import(string path)
    {
        var fullPath = Path.GetFullPath(path);
        string folder;
        string? temporary = null;
        if (Directory.Exists(fullPath)) folder = fullPath;
        else if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            temporary = Path.Combine(Path.GetTempPath(), "PcbExpo", Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(fullPath, temporary);
            folder = temporary;
        }
        else throw new FileNotFoundException("Не найдена папка или ZIP с Gerber.", fullPath);

        try
        {
            var layers = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(x => Extensions.Contains(Path.GetExtension(x)))
                .Select(x => new GerberLayer(x, Path.GetFileName(x), Classify(x), Path.GetRelativePath(folder, x)))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (layers.Count == 0) throw new InvalidDataException("В наборе нет Gerber/Excellon файлов.");

            var outline = layers.Where(x => x.Kind == GerberLayerKind.BoardOutline).ToArray();
            RectangleF? bounds = outline.Length == 1 ? MeasureBounds(outline[0].Path) : null;
            var fallback = false;
            if (bounds is null && outline.Length == 0)
            {
                var artwork = layers.Where(x => x.Kind is GerberLayerKind.TopCopper or GerberLayerKind.BottomCopper)
                    .Select(x => MeasureBounds(x.Path)).Where(x => x is not null).Select(x => x!.Value).ToArray();
                if (artwork.Length > 0)
                {
                    bounds = artwork.Aggregate(RectangleF.Union);
                    fallback = true;
                }
            }
            if (bounds is { Width: <= 0 } or { Height: <= 0 })
                throw new InvalidDataException("Границы платы имеют нулевой размер.");
            return new GerberPackage
            {
                SourcePath = fullPath,
                ProjectName = Path.GetFileNameWithoutExtension(fullPath),
                Layers = layers,
                BoardBoundsMm = bounds,
                OutlineFallback = fallback,
                TemporaryDirectory = temporary
            };
        }
        catch
        {
            if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            throw;
        }
    }

    public static GerberLayerKind Classify(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (ext is ".drl" or ".xln") return GerberLayerKind.Drill;
        if (ext is ".gko" or ".gm1" || name.Contains("outline") || name.Contains("edge_cuts") || name.Contains("edge-cuts")) return GerberLayerKind.BoardOutline;
        if (ext == ".gtl" || name.Contains("toplayer") || name.Contains("top_copper")) return GerberLayerKind.TopCopper;
        if (ext == ".gbl" || name.Contains("bottomlayer") || name.Contains("bottom_copper")) return GerberLayerKind.BottomCopper;
        if (ext == ".gts" || name.Contains("topsoldermask") || name.Contains("top_solder_mask")) return GerberLayerKind.TopSolderMask;
        if (ext == ".gbs" || name.Contains("bottomsoldermask") || name.Contains("bottom_solder_mask")) return GerberLayerKind.BottomSolderMask;
        if (ext == ".gtp" || name.Contains("toppastemask") || name.Contains("top_paste") || name.Contains("f_paste")) return GerberLayerKind.TopPasteMask;
        if (ext == ".gbp" || name.Contains("bottompastemask") || name.Contains("bottom_paste") || name.Contains("b_paste")) return GerberLayerKind.BottomPasteMask;
        if (name.Contains("silkscreen") || name.Contains("paste") || name.Contains("document")) return GerberLayerKind.Other;
        return GerberLayerKind.Unknown;
    }

    public static RectangleF? MeasureBounds(string gerberPath)
    {
        using var dummy = new Mat(1, 1, DepthType.Cv8U, 1);
        dummy.SetTo(new Emgu.CV.Structure.MCvScalar(0));
        return GerberFormat.ParseAndDraw(gerberPath, dummy, new SizeF(1, 1)).BoundsMm;
    }
}
