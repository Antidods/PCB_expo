using System.Text.Json.Serialization;

namespace PcbExpo.Core;

public enum ExposureMode { TopCopper, BottomCopper, TopSolderMask, BottomSolderMask, Registration, Calibration }
public enum PlacementMode { Single, FillBlank }
public enum BottomPhysicalFlip { VerticalAxis }

public sealed class BlankProfile
{
    public string Name { get; set; } = "FR4 150x100";
    public double WidthMm { get; set; } = 150;
    public double HeightMm { get; set; } = 100;
    public double RegistrationHoleDiameterMm { get; set; } = 4;
    public double HoleInsetXmm { get; set; } = 10;
    public double HoleInsetYmm { get; set; } = 10;
    public double HoleClearanceMm { get; set; } = 2;
    public double AlignmentPointDiameterMm { get; set; } = 0.2;
    public double AlignmentInsetXmm { get; set; } = 10;
    public double AlignmentInsetYmm { get; set; } = 10;
    public List<PointMm>? CustomAlignmentPoints { get; set; }
}

public sealed class PanelizationSettings
{
    public PlacementMode Mode { get; set; } = PlacementMode.Single;
    public double SpacingXmm { get; set; } = 2;
    public double SpacingYmm { get; set; } = 2;
    public double MarginLeftMm { get; set; }
    public double MarginRightMm { get; set; }
    public double MarginTopMm { get; set; }
    public double MarginBottomMm { get; set; }
}

public sealed class TransformationSettings
{
    public bool Invert { get; set; }
    public bool MirrorX { get; set; }
    public bool MirrorY { get; set; }
}

public sealed class ExposureSettings
{
    public double CopperSeconds { get; set; }
    public double SolderMaskSeconds { get; set; }
    public double RegistrationSeconds { get; set; }
    public double CalibrationSeconds { get; set; }
    public byte? LightPwm { get; set; }
    public double CopperCompensationMm { get; set; }
    public double SolderMaskCompensationMm { get; set; }
}

public sealed class ProjectModel
{
    public string TemplatePath { get; set; } = "";
    public string GerberSourcePath { get; set; } = "";
    public string Name { get; set; } = "PCB";
    public Dictionary<GerberLayerKind, string> LayerPaths { get; set; } = [];
    public BlankProfile Blank { get; set; } = new();
    public PointMm PcbPositionMm { get; set; } = new(20, 15);
    public PanelizationSettings Panelization { get; set; } = new();
    public ExposureMode Mode { get; set; } = ExposureMode.TopCopper;
    public BottomPhysicalFlip BottomFlip { get; set; } = BottomPhysicalFlip.VerticalAxis;
    public Dictionary<ExposureMode, TransformationSettings> Transformations { get; set; } = new()
    {
        [ExposureMode.TopCopper] = new(),
        [ExposureMode.BottomCopper] = new(),
        [ExposureMode.TopSolderMask] = new() { Invert = true },
        [ExposureMode.BottomSolderMask] = new() { Invert = true }
    };
    public ExposureSettings Exposure { get; set; } = new();
    public bool AntiAliasing { get; set; }

    [JsonIgnore]
    public TransformationSettings CurrentTransform => Transformations.TryGetValue(Mode, out var value)
        ? value : Transformations[Mode] = new TransformationSettings();

    public GerberLayerKind CurrentLayerKind => Mode switch
    {
        ExposureMode.TopCopper => GerberLayerKind.TopCopper,
        ExposureMode.BottomCopper => GerberLayerKind.BottomCopper,
        ExposureMode.TopSolderMask => GerberLayerKind.TopSolderMask,
        ExposureMode.BottomSolderMask => GerberLayerKind.BottomSolderMask,
        _ => GerberLayerKind.Unknown
    };

    public double CurrentExposureSeconds => Mode switch
    {
        ExposureMode.TopCopper or ExposureMode.BottomCopper => Exposure.CopperSeconds,
        ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask => Exposure.SolderMaskSeconds,
        ExposureMode.Registration => Exposure.RegistrationSeconds,
        ExposureMode.Calibration => Exposure.CalibrationSeconds,
        _ => 0
    };

    public double CurrentCompensationMm => Mode switch
    {
        ExposureMode.TopCopper or ExposureMode.BottomCopper => Exposure.CopperCompensationMm,
        ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask => Exposure.SolderMaskCompensationMm,
        _ => 0
    };

    public bool IsBottom => Mode is ExposureMode.BottomCopper or ExposureMode.BottomSolderMask;
}
