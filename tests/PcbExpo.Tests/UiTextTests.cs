using System.Globalization;
using System.Text.Json;
using PcbExpo.App;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class UiTextTests
{
    [Theory]
    [InlineData("1,25", 1.25)]
    [InlineData("1.25", 1.25)]
    [InlineData("0,016742", 0.016742)]
    public void RussianNumericInputAcceptsCommaAndDot(string input, double expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.True(UiText.TryNumber(input, out var actual));
            Assert.Equal(expected, actual);
            Assert.True(UiText.TryNumber(UiText.Number(actual), out var roundTrip));
            Assert.Equal(expected, roundTrip);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RussianLabelsDoNotChangeProjectEnumIdentifiers()
    {
        var project = new ProjectModel { Mode = ExposureMode.BottomSolderMask };
        project.Panelization.Mode = PlacementMode.FillBlank;
        Assert.Equal("Паяльная маска Bottom", UiText.Exposure(project.Mode));
        Assert.Equal("Заполнить заготовку", UiText.Placement(project.Panelization.Mode));
        var json = JsonSerializer.Serialize(project, LocalStorage.JsonOptions);
        Assert.Contains("\"BottomSolderMask\"", json);
        Assert.Contains("\"FillBlank\"", json);
        var restored = JsonSerializer.Deserialize<ProjectModel>(json, LocalStorage.JsonOptions);
        Assert.Equal(project.Mode, restored!.Mode);
        Assert.Equal(project.Panelization.Mode, restored.Panelization.Mode);
    }

    [Fact]
    public void SavedGerberAssignmentOverridesAutomaticClassification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcbexpo-project-{Guid.NewGuid():N}.pcbexpo.json");
        try
        {
            var project = new ProjectModel();
            project.LayerNames[GerberLayerKind.TopCopper] = "custom/selected.gbr";
            var persistence = new ProjectPersistenceService();
            persistence.Save(project, path);
            var loaded = persistence.Load(path);
            var package = new GerberPackage
            {
                Layers =
                [
                    new("default.gtl", "default.gtl", GerberLayerKind.TopCopper, "default.gtl"),
                    new("selected.gbr", "selected.gbr", GerberLayerKind.Unknown, "custom/selected.gbr")
                ]
            };
            loaded.LayerPaths[GerberLayerKind.TopCopper] = "default.gtl";
            LayerAssignments.Restore(loaded, package, loaded.LayerNames.ToDictionary());
            Assert.Equal("selected.gbr", loaded.LayerPaths[GerberLayerKind.TopCopper]);
            Assert.Equal("custom/selected.gbr", loaded.LayerNames[GerberLayerKind.TopCopper]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
