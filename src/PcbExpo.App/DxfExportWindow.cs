using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcbExpo.Core;

namespace PcbExpo.App;

internal sealed class DxfExportWindow : Window
{
    private readonly ComboBox _source = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _selection = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _export = new() { Content = "Сохранить DXF", IsEnabled = false };
    private readonly DxfPreviewControl _preview = new();
    private readonly StackPanel _cncOptions = new() { Spacing = 6, IsVisible = false };
    private readonly TextBlock _selectionLabel = new() { Text = "Какие контуры сохранить" };
    private DxfSource? _optionsSource;
    private IReadOnlyList<RectMm> _boards = [];
    private IReadOnlyList<DxfContour> _contours = [];
    private int _generation;
    private readonly string _projectName;
    private readonly HashSet<string> _protectedPaths;
    public Action<string, int>? Exported { get; init; }

    public DxfExportWindow(string projectName, IReadOnlyList<DxfSource> sources, IEnumerable<string> protectedPaths)
    {
        _projectName = projectName;
        _protectedPaths = protectedPaths.Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Title = "Экспорт контуров в DXF";
        Width = 1100; Height = 720; MinWidth = 850; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UiTheme.FitToScreen(this);
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), ColumnDefinitions = new ColumnDefinitions("360,*") };
        var content = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Источник контуров", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = UiTheme.Brush("#10173A") });
        _source.ItemsSource = sources; _source.SelectedIndex = 0;
        content.Children.Add(_source);
        content.Children.Add(UiTheme.Note(_description));
        content.Children.Add(_cncOptions);
        content.Children.Add(_selectionLabel);
        content.Children.Add(_selection);
        content.Children.Add(new TextBlock
        {
            Text = "DXF в миллиметрах, масштаб 1:1; X вправо, Y вверх. Для CNC выбранный состав всегда сохраняется для всей раскладки. Для остальных источников можно сохранить все контуры или один выбранный.",
            TextWrapping = TextWrapping.Wrap, Classes = { "caption" }
        });
        root.Children.Add(new Border { Classes = { "panel" }, Child = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } });
        var previewPanel = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(14) };
        previewPanel.Children.Add(new Border { Child = _preview, BorderBrush = UiTheme.Brush("#C9CED8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
        var legend = new TextBlock { Text = "Зелёный: контуры плат. Голубой: сверловка и пазы. Серый: заготовка, центровочные отверстия и рамки позиций плат. Колесо: масштаб; правая кнопка: сдвиг.",
            TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(legend, 1); previewPanel.Children.Add(legend);
        Grid.SetColumn(previewPanel, 1); root.Children.Add(previewPanel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10 };
        var close = new Button { Content = "Закрыть" };
        close.Click += (_, _) => Close();
        buttons.Children.Add(close); buttons.Children.Add(_export);
        _export.Classes.Add("primary");
        _status.Classes.Add("caption");
        var footerContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        _status.VerticalAlignment = VerticalAlignment.Center;
        footerContent.Children.Add(_status); Grid.SetColumn(buttons, 1); footerContent.Children.Add(buttons);
        var footer = UiTheme.Footer(footerContent);
        Grid.SetRow(footer, 1); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
        UiTheme.Ellipsis(_source); UiTheme.Ellipsis(_selection);
        Content = root;
        _source.SelectionChanged += async (_, _) => await LoadSource();
        _selection.SelectionChanged += (_, _) =>
        {
            ToolTip.SetTip(_selection, _selection.SelectedItem?.ToString());
            if (_selection.SelectedItem is DisplayChoice<int> choice && _source.SelectedItem is DxfSource selectedSource)
                _preview.SetContours(choice.Value < 0 ? _contours : [_contours[choice.Value]], selectedSource.ViewBounds, _boards);
        };
        _export.Click += async (_, _) => await Export();
        Opened += async (_, _) => await LoadSource();
        Closed += (_, _) => _generation++;
    }

    private async Task LoadSource()
    {
        var generation = ++_generation;
        _contours = []; _selection.ItemsSource = null; _export.IsEnabled = false;
        _preview.SetContours([]);
        if (_source.SelectedItem is not DxfSource source) return;
        ConfigureCncOptions(source);
        _description.Text = source.Description;
        ToolTip.SetTip(_source, source.Label);
        _status.Text = "Построение контуров…";
        try
        {
            _boards = source.BoardLayout?.Invoke() ?? [];
            _preview.SetContours([], source.ViewBounds, _boards);
            var snapshot = source.CncSettings?.Snapshot();
            var contours = await Task.Run(() => snapshot is not null && source.BuildCnc is { } buildCnc
                ? buildCnc(snapshot) : source.Build());
            if (generation != _generation) return;
            _contours = contours;
            _selection.ItemsSource = new[] { new DisplayChoice<int>(-1, $"Все контуры ({contours.Count})") }
                .Concat(source.CncSettings is null ? contours.Select((c, i) => new DisplayChoice<int>(i, Label(c, i))) : []).ToArray();
            _selection.SelectedIndex = 0;
            _export.IsEnabled = contours.Count > 0;
            _status.Text = $"Контуров: {contours.Count}; круглых отверстий: {contours.OfType<DxfCircle>().Count()}; пазов: {contours.Count(c => c.Layer.EndsWith("_SLOTS", StringComparison.Ordinal))}.";
        }
        catch (Exception error)
        {
            if (generation == _generation) _status.Text = error.Message;
        }
    }

    private void ConfigureCncOptions(DxfSource source)
    {
        var options = source.CncSettings;
        _cncOptions.IsVisible = options is not null;
        _selection.IsVisible = _selectionLabel.IsVisible = options is null;
        if (ReferenceEquals(_optionsSource, source)) return;
        _optionsSource = source;
        _cncOptions.Children.Clear();
        if (options is null) return;
        _cncOptions.Children.Add(new TextBlock { Text = "Состав DXF для всей раскладки", FontWeight = FontWeight.Bold });
        _cncOptions.Children.Add(UiTheme.Caption("Заготовка"));
        Add("Контур текстолита", options.BlankOutline, v => options.BlankOutline = v);
        Add("Центровочные отверстия текстолита", options.RegistrationHoles, v => options.RegistrationHoles = v);
        _cncOptions.Children.Add(UiTheme.Caption("Платы"));
        Add("Внешние контуры плат", options.BoardOutlines, v => options.BoardOutlines = v);
        Add("Внутренние вырезы плат", options.BoardCutouts, v => options.BoardCutouts = v);
        Add("Механические отверстия на платах", options.BoardHoles, v => options.BoardHoles = v);
        _cncOptions.Children.Add(UiTheme.Caption("Сверловка"));
        Add("Сверловка переходных отверстий", options.Vias, v => options.Vias = v);
        Add("Отверстия для компонентов", options.ComponentHoles, v => options.ComponentHoles = v);
        _cncOptions.Children.Add(new TextBlock
        {
            Text = "Назначение сверловки предложено по именам файлов (Via / NPTH / PTH). Проверьте его; неизвестные файлы относятся к механическим отверстиям. Для смешанного файла сначала разделите операции в CAD.",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var file in source.DrillFiles ?? [])
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170"), ColumnSpacing = 8 };
            var name = new TextBlock { Text = file.RelativePath, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            ToolTip.SetTip(name, file.RelativePath); row.Children.Add(name);
            var category = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new DisplayChoice<CncDrillKind>[]
                {
                    new(CncDrillKind.BoardHole, "Механические отверстия"),
                    new(CncDrillKind.Via, "Переходные отверстия"),
                    new(CncDrillKind.Component, "Отверстия компонентов")
                } };
            category.SelectedItem = category.ItemsSource.Cast<DisplayChoice<CncDrillKind>>()
                .Single(c => c.Value == options.KindFor(file));
            category.SelectionChanged += async (_, _) =>
            {
                if (category.SelectedItem is not DisplayChoice<CncDrillKind> choice) return;
                options.DrillKinds[file.RelativePath] = choice.Value;
                await LoadSource();
            };
            UiTheme.Ellipsis(category); Grid.SetColumn(category, 1); row.Children.Add(category); _cncOptions.Children.Add(row);
        }
        void Add(string label, bool value, Action<bool> set)
        {
            var checkbox = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value };
            checkbox.IsCheckedChanged += async (_, _) => { set(checkbox.IsChecked == true); await LoadSource(); };
            _cncOptions.Children.Add(checkbox);
        }
    }

    private async Task Export()
    {
        if (_source.SelectedItem is not DxfSource source || _selection.SelectedItem is not DisplayChoice<int> selection) return;
        IReadOnlyList<DxfContour> selected = source.CncSettings is not null || selection.Value < 0 ? _contours : [_contours[selection.Value]];
        _export.IsEnabled = false; _source.IsEnabled = false; _selection.IsEnabled = false;
        _cncOptions.IsEnabled = false;
        try
        {
            var name = UiText.SafeFileName(_projectName);
            var path = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Сохранить контуры в DXF", SuggestedFileName = $"{name}_{source.FileStem}.dxf",
                DefaultExtension = "dxf", ShowOverwritePrompt = true,
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("DXF") { Patterns = ["*.dxf"] }]
            });
            if (path is null) return;
            var output = path.Path.LocalPath;
            if (_protectedPaths.Contains(Path.GetFullPath(output)))
                throw new InvalidOperationException("Нельзя перезаписывать исходный Gerber, ZIP или шаблон. Выберите другое имя файла.");
            await Task.Run(() => new DxfExportService().Export(output, selected));
            Exported?.Invoke(output, selected.Count);
            Close();
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _export.IsEnabled = true; _source.IsEnabled = true; _selection.IsEnabled = true; _cncOptions.IsEnabled = true; }
    }

    private static string Label(DxfContour contour, int index) => contour switch
    {
        DxfCircle circle => $"{index + 1}. {circle.Layer}: Ø {UiText.Number(circle.RadiusMm * 2)} мм; X/Y {UiText.Number(circle.Center.X)} / {UiText.Number(circle.Center.Y)}",
        DxfPolyline line => $"{index + 1}. {(line.Closed ? "Замкнутый" : "Открытый")} контур; {line.Points.Count} вершин; X/Y {UiText.Number(line.Points[0].X)} / {UiText.Number(line.Points[0].Y)}",
        _ => $"Контур {index + 1}"
    };
}
