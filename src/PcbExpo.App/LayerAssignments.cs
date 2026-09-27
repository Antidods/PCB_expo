using PcbExpo.Core;

namespace PcbExpo.App;

internal static class LayerAssignments
{
    public static void Restore(ProjectModel project, GerberPackage package,
        IReadOnlyDictionary<GerberLayerKind, string> savedNames)
    {
        if (savedNames.Count == 0) return;
        var resolved = savedNames.Select(pair =>
        {
            var found = package.Layers.FirstOrDefault(x =>
                string.Equals(x.RelativePath, pair.Value, StringComparison.OrdinalIgnoreCase));
            if (found is null)
                throw new FileNotFoundException($"Назначенный слой {UiText.Layer(pair.Key)} не найден в наборе Gerber.", pair.Value);
            return (pair.Key, Layer: found);
        }).ToArray();

        project.LayerPaths.Clear();
        project.LayerNames.Clear();
        foreach (var (kind, layer) in resolved)
        {
            project.LayerPaths[kind] = layer.Path;
            project.LayerNames[kind] = layer.RelativePath;
            if (kind != GerberLayerKind.BoardOutline) continue;
            package.BoardBoundsMm = GerberImportService.MeasureBounds(layer.Path);
            package.OutlineFallback = false;
        }
    }
}
