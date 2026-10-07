using System.Globalization;
using System.Text.Json;
using PcbExpo.App;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public class UiTextTests
{
    [Fact]
    public void StructuredSummaryPreservesExportTextAndWarnings()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var printer = new PrinterInfo("template.cxdlpv4", "CXDLPV4", 10000, 5000, 200, 150, 0.02, 0.03, 1, 60, 255);
            var project = new ProjectModel { Mode = ExposureMode.TopCopper, PcbPositionMm = new PointMm(20, 15) };
            project.Blank.WidthMm = 150; project.Blank.HeightMm = 100;
            project.Exposure.CopperSeconds = 60; project.Exposure.CopperCompensationMm = 0.025;
            project.CurrentTransform.MirrorX = true; project.CurrentTransform.MirrorY = false;
            project.CurrentTransform.Invert = false; project.AntiAliasing = false;
            using var package = new GerberPackage { BoardBoundsMm = new System.Drawing.RectangleF(0, 0, 35.56f, 25.4f), OutlineFallback = true };
            RectMm[] positions = [new(20, 15, 35.56, 25.4), new(60, 15, 35.56, 25.4)];
            var parts = UiText.SummaryParts(printer, project, package, 2, positions);
            Assert.Equal(2, parts.Warnings.Count);
            Assert.Equal(new[] { "Принтер и шаблон", "Плата и заготовка", "Экспозиция" }, parts.Groups.Select(g => g.Title));
            Assert.Equal("""
                Формат: CXDLPV4
                Разрешение: 10000 × 5000 px
                Поле LCD: 200 × 150 мм
                Шаг пикселя: X 0.020000, Y 0.030000 мм
                Шаблон: template.cxdlpv4
                ВНИМАНИЕ: шаг пикселя X/Y различается. Проверьте масштаб калибровкой на LCD.
                ВНИМАНИЕ: контур платы отсутствует; габариты оценены по artwork.
                Плата: 35.560001 × 25.4 мм
                Заготовка: 150 × 100 мм
                Режим: Медь Top
                Плат: 2
                Плата X/Y: 20 / 15 мм
                Позиции на заготовке: (20, 15); (60, 15)
                Зеркалирование X/Y: да / нет
                Инверсия: нет
                Экспозиция: 60 с
                Компенсация: 0.025 мм
                PWM: из шаблона
                Физический переворот Bottom: относительно вертикальной оси центра заготовки
                Сглаживание: нет
                Белое на preview означает, что LCD пропускает UV.
                """.ReplaceLineEndings("\n"), UiText.Summary(printer, project, package, 2, positions));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void CalibrationSummaryShowsEffectiveTransformsAndColumnCompensations()
    {
        var printer = new PrinterInfo("template.cxdlpv4", "CXDLPV4", 1000, 500, 200, 100, 0.2, 0.2, 1, 60, 255);
        var project = new ProjectModel { Mode = ExposureMode.ExposureCalibration, AntiAliasing = true };
        project.CurrentTransform.MirrorX = project.CurrentTransform.MirrorY = true;
        var parts = UiText.SummaryParts(printer, project, null, 0, []);
        Assert.Empty(parts.Warnings);
        var fields = parts.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Label, f => f.Value);
        Assert.Equal("нет / нет", fields["Зеркалирование X/Y"]);
        Assert.Equal("нет", fields["Сглаживание"]);
        Assert.Equal("не загружена", fields["Плата"]);
        Assert.Contains("Компенсации столбцов", fields.Keys);
        Assert.DoesNotContain("Компенсация", fields.Keys);
    }

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
