namespace PcbExpo.Core;

public enum CncDrillKind { BoardHole, Via, Component }

public sealed class CncExportSettings
{
    public bool BlankOutline { get; set; } = true;
    public bool RegistrationHoles { get; set; } = true;
    public bool BoardOutlines { get; set; } = true;
    public bool BoardCutouts { get; set; } = true;
    public bool BoardHoles { get; set; } = true;
    public bool Vias { get; set; } = true;
    public bool ComponentHoles { get; set; } = true;
    public Dictionary<string, CncDrillKind> DrillKinds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CncExportSettings Snapshot() => new()
    {
        BlankOutline = BlankOutline, RegistrationHoles = RegistrationHoles,
        BoardOutlines = BoardOutlines, BoardCutouts = BoardCutouts,
        BoardHoles = BoardHoles, Vias = Vias, ComponentHoles = ComponentHoles,
        DrillKinds = new Dictionary<string, CncDrillKind>(DrillKinds, StringComparer.OrdinalIgnoreCase)
    };

    public CncDrillKind KindFor(GerberLayer layer) => DrillKinds.TryGetValue(layer.RelativePath, out var kind)
        ? kind : SuggestKind(layer.Name);

    public static CncDrillKind SuggestKind(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("via")) return CncDrillKind.Via;
        if (lower.Contains("npth")) return CncDrillKind.BoardHole;
        if (lower.Contains("pth")) return CncDrillKind.Component;
        return CncDrillKind.BoardHole;
    }

    public bool Includes(CncDrillKind kind) => kind switch
    {
        CncDrillKind.BoardHole => BoardHoles,
        CncDrillKind.Via => Vias,
        CncDrillKind.Component => ComponentHoles,
        _ => false
    };
}
