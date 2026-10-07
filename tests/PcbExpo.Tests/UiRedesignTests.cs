using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcbExpo.App;
using PcbExpo.Core;

namespace PcbExpo.Tests;

public sealed class UiRedesignTests
{
    [Fact]
    public async Task WindowsRenderAndKeepOperatorControlsAccessible()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestAppBuilder));
        await session.Dispatch(async () =>
        {
            var main = new MainWindow();
            main.Show();
            try
            {
                Capture(main, "main-empty");
                Assert.Contains(Texts(main), t => t.Text == "Плата не загружена" && t.IsEffectivelyVisible);
                Assert.Contains(Texts(main), t => t.Text == "Слои Gerber и сверловка");
                var layersSection = main.GetVisualDescendants().OfType<Expander>().First();
                Click(layersSection.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("expander-header")));
                Assert.False(layersSection.IsExpanded);
                Click(layersSection.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("expander-header")));
                Assert.True(layersSection.IsExpanded);
                foreach (var (width, height) in new[] { (1560, 940), (1366, 768), (1280, 672), (1100, 700), (1100, 600) })
                {
                    main.Width = width; main.Height = height;
                    Capture(main, $"main-{width}x{height}");
                    foreach (var button in main.GetVisualDescendants().OfType<Button>()
                        .Where(b => b.Classes.Contains("toolbar") || ButtonText(b) == "Экспортировать CXDLPV4"))
                        AssertInside(main, button);
                    Assert.True(main.GetVisualDescendants().OfType<PreviewControl>().Single().Bounds.Width >= 420);
                }
                var chips = main.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("chip")).ToArray();
                Click(chips.Single(b => ButtonText(b) == "Калибровка времени и компенсации"));
                Capture(main, "main-calibration");
                Assert.Contains("on", chips.Single(b => ButtonText(b) == "Калибровка времени и компенсации").Classes);
                Assert.All(main.GetVisualDescendants().OfType<CheckBox>()
                    .Where(c => c.Content?.ToString() is "Зеркалирование X" or "Зеркалирование Y"), c => Assert.False(c.IsEnabled));
                var compensation = main.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => ToolTip.GetTip(t)?.ToString() == "Компенсация экспозиции, мм");
                Assert.False(compensation.IsEnabled);
                Click(chips.Single(b => ButtonText(b) == "Точки центровки"));
                Capture(main, "main-registration");
                Assert.True(compensation.IsEnabled);
                var x = main.GetVisualDescendants().OfType<TextBox>().Single(t => ToolTip.GetTip(t)?.ToString() == "Плата X");
                x.Text = "не число";
                x.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Capture(main, "main-invalid-number");
                Assert.Contains(Texts(main), t => t.Text == "Плата X: введите число.");
                Assert.True(UiText.TryNumber(x.Text, out _));

                var sourceRoot = FindSourceRoot();
                if (sourceRoot is not null)
                {
                    var gerber = Path.Combine(sourceRoot, "Gerber_Cube_PCB_Cube_2026-09-23");
                    if (Directory.Exists(gerber))
                    {
                        var template = Path.Combine(sourceRoot, "150x100.cxdlpv4");
                        if (File.Exists(template))
                            typeof(MainWindow).GetMethod("LoadTemplate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [template]);
                        Assert.True((bool)typeof(MainWindow).GetMethod("Import", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(main, [gerber])!);
                        var mode = main.GetVisualDescendants().OfType<ComboBox>()
                            .Single(c => c.ItemsSource is DisplayChoice<ExposureMode>[]);
                        mode.SelectedIndex = 0;
                        await WaitUntil(() => !main.GetVisualDescendants().OfType<Border>()
                            .Any(b => b.IsVisible && b.Child is StackPanel p && p.Children.OfType<ProgressBar>().Any()));
                        main.Width = 1560; main.Height = 940;
                        Capture(main, "main-loaded");
                        if (File.Exists(template))
                        {
                            Assert.Contains(Texts(main), t => t.Text?.StartsWith("Предпросмотр:") == true);
                            var placement = main.GetVisualDescendants().OfType<ComboBox>()
                                .Single(c => c.ItemsSource is DisplayChoice<PcbExpo.Core.PlacementMode>[]);
                            placement.SelectedIndex = 1;
                            await WaitUntil(() => !PrivateField<Border>(main, "_previewProgress").IsVisible);
                            Capture(main, "main-filled");
                            var count = PrivateField<int>(main, "_boardCount");
                            Assert.True(count > 1);
                            Assert.Contains($"Плат: {count}", PrivateField<TextBlock>(main, "_statusBar").Text);
                            foreach (var exposureMode in new[] { ExposureMode.Calibration, ExposureMode.ExposureCalibration })
                            {
                                mode.SelectedItem = mode.ItemsSource!.Cast<DisplayChoice<ExposureMode>>().Single(c => c.Value == exposureMode);
                                await WaitUntil(() => !PrivateField<Border>(main, "_previewProgress").IsVisible);
                                Capture(main, exposureMode == ExposureMode.Calibration ? "main-scale-calibration" : "main-process-calibration");
                                Assert.Contains(Texts(main), t => t.Text?.StartsWith("Предпросмотр:") == true);
                            }
                            mode.SelectedIndex = 0;
                            await WaitUntil(() => !PrivateField<Border>(main, "_previewProgress").IsVisible);
                            var confirmation = (Task<bool>)typeof(MainWindow).GetMethod("ConfirmExport", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [])!;
                            var dialog = main.OwnedWindows.Single();
                            Capture(dialog, "confirm-export");
                            Click(Buttons(dialog).Single(b => ButtonText(b) == "Отмена"));
                            Assert.False(await confirmation);
                            var activeProject = PrivateField<ProjectModel>(main, "_project");
                            var sources = new DxfSourceCatalog(PrivateField<ExposureRasterService>(main, "_raster"))
                                .Create(activeProject, PrivateField<GerberPackage>(main, "_package"), PrivateField<PrinterInfo>(main, "_printer"), true);
                            var cnc = new DxfExportWindow("Проверка CNC", sources, []);
                            cnc.Show();
                            try
                            {
                                await WaitUntil(() => Buttons(cnc).Single(b => ButtonText(b) == "Сохранить DXF").IsEnabled);
                                Capture(cnc, "dxf-cnc");
                                var categories = cnc.GetVisualDescendants().OfType<ComboBox>()
                                    .Where(c => c.ItemsSource is DisplayChoice<CncDrillKind>[]).ToArray();
                                Assert.NotEmpty(categories);
                                var category = categories[0]; category.SelectedIndex = (category.SelectedIndex + 1) % 3;
                                await WaitUntil(() => Buttons(cnc).Single(b => ButtonText(b) == "Сохранить DXF").IsEnabled);
                                var via = cnc.GetVisualDescendants().OfType<CheckBox>()
                                    .Single(c => c.Content is TextBlock t && t.Text == "Сверловка переходных отверстий");
                                via.IsChecked = !via.IsChecked;
                                await WaitUntil(() => Buttons(cnc).Single(b => ButtonText(b) == "Сохранить DXF").IsEnabled);
                                Capture(cnc, "dxf-cnc-updated");
                            }
                            finally { cnc.Close(); }
                        }
                    }
                }

                var project = new ProjectModel();
                project.Exposure.ProcessCalibrationSeconds = 90;
                CalibrationApplyTarget? applied = null;
                var calibration = new ExposureCalibrationWindow(project, null, (settings, time, target) =>
                {
                    project.ProcessCalibration = settings;
                    Assert.Equal(90, time); applied = target;
                });
                calibration.Show();
                try
                {
                    Capture(calibration, "calibration");
                    Click(Buttons(calibration).Single(b => ButtonText(b).StartsWith("Заполнить список:")));
                    Assert.Contains(calibration.GetVisualDescendants().OfType<TextBox>(),
                        t => t.Text == string.Join("; ", new[] { 54d, 72, 90, 108, 126 }.Select(UiText.Number)));
                    Click(Buttons(calibration).Single(b => ButtonText(b) == "Применить к меди"));
                    Assert.Equal(CalibrationApplyTarget.Copper, applied);
                    Assert.Equal(new[] { 54d, 72, 90, 108, 126 }, project.ProcessCalibration.TimesSeconds);
                    calibration.Width = 540; calibration.Height = 450;
                    Capture(calibration, "calibration-minimum");
                    foreach (var button in Buttons(calibration).Where(b => ButtonText(b).StartsWith("Применить") || ButtonText(b) == "Закрыть"))
                        AssertInside(calibration, button);
                }
                finally { calibration.Close(); }

                var dxf = new DxfExportWindow("Проверка", [new DxfSource("Заготовка", "blank", "Контур заготовки, масштаб 1:1.",
                    () => [new DxfCircle("TEST", new PointMm(50, 50), 2)])], []);
                dxf.Show();
                try
                {
                    await Task.Delay(200);
                    Capture(dxf, "dxf");
                    var save = Buttons(dxf).Single(b => ButtonText(b) == "Сохранить DXF");
                    Assert.True(save.IsEnabled); AssertInside(dxf, save);
                }
                finally { dxf.Close(); }
                foreach (var window in new Window[] { new CalibrationHelpWindow(), new AboutWindow() })
                {
                    window.Show();
                    try { Capture(window, window is AboutWindow ? "about" : "help"); }
                    finally { window.Close(); }
                }
                return true;
            }
            finally { main.Close(); }
        }, CancellationToken.None);
    }

    private static IEnumerable<TextBlock> Texts(Window window) => window.GetVisualDescendants().OfType<TextBlock>();
    private static IEnumerable<Button> Buttons(Window window) => window.GetVisualDescendants().OfType<Button>();
    private static T PrivateField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static string ButtonText(Button button) => button.Content is TextBlock text ? text.Text ?? "" : button.Content?.ToString() ?? "";
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitUntil(Func<bool> complete)
    {
        for (var i = 0; i < 300 && !complete(); i++) await Task.Delay(50);
        Assert.True(complete(), "Предпросмотр не завершён за 15 с.");
    }

    private static void AssertInside(Window window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= 0 && point.Y >= 0 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 &&
            point.Y + control.Bounds.Height <= window.Bounds.Height + 1, $"{control}: {point}, {control.Bounds}; window {window.Bounds}");
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("PCBEXPO_UI_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); frame.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        File.WriteAllLines(Path.Combine(directory, name + "-tree.txt"), window.GetVisualDescendants().OfType<Control>()
            .Select(c => $"{c.GetType().Name} #{c.Name} [{string.Join(' ', c.Classes)}] {c.Bounds} " +
                (c is Border b ? $"bg={b.Background}; border={b.BorderBrush} {b.BorderThickness}" :
                 c is TextBlock t ? $"font={t.FontSize} {t.FontFamily} {t.FontWeight}; fg={t.Foreground}; {t.Text}" :
                 c is Button button ? $"bg={button.Background}; border={button.BorderBrush} {button.BorderThickness}; font={button.FontSize}; fg={button.Foreground}" : "")));
    }

    private static string? FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PcbExpo.slnx"))) return directory.FullName;
        return null;
    }
}

public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PcbExpoApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .With(new FontManagerOptions { DefaultFamilyName = "Segoe UI" });
}
