using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Emgu.CV;
using System.Text.Json;
using PcbExpo.Core;
using CorePlacementMode = PcbExpo.Core.PlacementMode;

namespace PcbExpo.App;

public sealed class MainWindow : Window
{
    private ProjectModel _project = new();
    private GerberPackage? _package;
    private PrinterInfo? _printer;
    private readonly Cxdlpv4TemplateService _template = new();
    private readonly GerberImportService _importer = new();
    private readonly ProjectPersistenceService _projects = new();
    private readonly BlankProfileService _profiles = new();
    private readonly ExposureProfileService _exposureProfiles = new();
    private readonly ApplicationLog _log = new();
    private readonly CoordinateTransformService _coordinates = new();
    private readonly BlankLayoutService _layout = new();
    private readonly ExposureRasterService _raster;
    private readonly PreviewControl _preview = new();
    private readonly Border _previewProgress = new()
    {
        IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(12), Classes = { "note" },
        Child = new StackPanel { Spacing = 8, Children =
        {
            UiTheme.Caption("Обновление предпросмотра…"),
            new ProgressBar { IsIndeterminate = true, Height = 3, Width = 220 }
        } }
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Border _statusBorder = new() { Classes = { "alert" } };
    private readonly StackPanel _summary = new() { Spacing = 12 };
    private readonly StackPanel _warnings = new() { Spacing = 8 };
    private readonly TextBlock _statusBar = new() { FontSize = 11.5, Foreground = UiTheme.Brush("#596174") };
    private readonly ListBox _files = new() { MaxHeight = 118 };
    private readonly Dictionary<ExposureMode, Button> _modeButtons = [];
    private Border _calibrationNote = null!;
    private Border _stencilNote = null!;
    private Border _processCalibrationNote = null!;
    private Border _emptyPreview = null!;
    private Button _seriesButton = null!;
    private IReadOnlyList<string> _importWarnings = [];
    private readonly ComboBox _mode = new();
    private readonly ComboBox _layer = new();
    private readonly ComboBox _outline = new();
    private readonly ComboBox _drill = new();
    private readonly ComboBox _placement = new();
    private readonly ComboBox _profile = new();
    private readonly TextBlock _calibrationHint = new()
    {
        Text = "Эталон: линия 100 мм и квадрат 50 × 50 мм по внешним границам. Толщина задаётся в профиле заготовки. Минимальная заготовка 110 × 70 мм. Порядок измерений — в справке.",
        TextWrapping = TextWrapping.Wrap, IsVisible = false
    };
    private readonly StackPanel _processCalibrationPanel = new() { Spacing = 6, IsVisible = false };
    private readonly TextBlock _stencilHint = new()
    {
        Text = "Трафарет из фотополимера: белая заготовка, тёмные окна PasteMaskLayer и центровочные отверстия. После засветки снимите трафарет с FEP, отмойте и досветите. Время задаётся отдельно для материала и толщины.",
        TextWrapping = TextWrapping.Wrap, IsVisible = false
    };
    private bool _exportingCalibration;
    private readonly CheckBox _invert = new() { Content = "Инверсия" };
    private readonly CheckBox _mirrorX = new() { Content = "Зеркалирование X" };
    private readonly CheckBox _mirrorY = new() { Content = "Зеркалирование Y" };
    private readonly CheckBox _aa = new() { Content = "Сглаживание (дополнительно; по умолчанию выключено)" };
    private TextBox _x = null!;
    private TextBox _y = null!;
    private TextBox _time = null!;
    private TextBox _compensation = null!;
    private TextBox _pwm = null!;
    private bool _syncing;
    private int _boardCount;
    private IReadOnlyList<RectMm> _positions = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly SemaphoreSlim _previewBuild = new(1, 1);
    private int _previewGeneration;
    private bool _closed;

    public MainWindow()
    {
        _raster = new ExposureRasterService(_coordinates, _layout,
            new PanelizationService(_layout), new GerberRenderService(), new ExposureMaskService());
        Title = $"PCB Expo {AppVersion.Current} | HALOT-MAGE S";
        Width = 1560;
        Height = 940;
        MinWidth = 1100;
        MinHeight = 600;
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("44,*,26"), ColumnDefinitions = new ColumnDefinitions("320,*,280")
        };
        Content = root;
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        var logo = new Image { Source = AppBranding.Logo, Width = 102, Height = 34, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(logo, BitmapInterpolationMode.HighQuality);
        toolbar.Children.Add(logo);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        commands.Children.Add(Toolbar("Открыть папку Gerber", "M1,4 L6,4 L8,6 L15,6 L15,14 L1,14 Z", async () => await OpenFolder()));
        commands.Children.Add(Toolbar("Открыть ZIP с Gerber", "M3,1 L11,1 L14,4 L14,15 L3,15 Z M8,3 L8,12", async () => await OpenZip()));
        commands.Children.Add(Toolbar("Загрузить шаблон", "M2,3 L14,3 L14,14 L2,14 Z M5,7 L11,7 M5,10 L11,10", async () => await OpenTemplate()));
        commands.Children.Add(new Border { Width = 1, Height = 20, Background = UiTheme.Brush("#DDE1E8"), Margin = new Thickness(6, 0) });
        commands.Children.Add(Toolbar("Открыть проект", "M1,4 L6,4 L8,6 L15,6 L15,14 L1,14 Z", async () => await OpenProject()));
        commands.Children.Add(Toolbar("Сохранить проект", "M2,1 L12,1 L15,4 L15,15 L2,15 Z M5,1 L5,6 L11,6 L11,1 M5,10 L12,10 L12,15 L5,15 Z", async () => await SaveProject()));
        Grid.SetColumn(commands, 1); toolbar.Children.Add(commands);
        var help = new Button { Content = "Справка ▾", Classes = { "toolbar" }, VerticalAlignment = VerticalAlignment.Center };
        var calibrationHelp = new MenuItem { Header = "Справка по калибровке" };
        calibrationHelp.Click += async (_, _) => await new CalibrationHelpWindow().ShowDialog(this);
        var about = new MenuItem { Header = "О программе" };
        about.Click += async (_, _) => await new AboutWindow().ShowDialog(this);
        help.Flyout = new MenuFlyout { Items = { calibrationHelp, about } };
        Grid.SetColumn(help, 2); toolbar.Children.Add(help);
        var toolbarBorder = new Border { Child = toolbar, Classes = { "panel" }, Padding = new Thickness(14, 4) };
        Grid.SetColumnSpan(toolbarBorder, 3); root.Children.Add(toolbarBorder);

        var editor = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        var left = new Border { Classes = { "panel" }, Child = new ScrollViewer { Content = editor, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Grid.SetRow(left, 1); root.Children.Add(left);
        editor.Children.Add(UiTheme.Heading("Режим экспозиции"));
        _mode.ItemsSource = Enum.GetValues<ExposureMode>()
            .Select(x => new DisplayChoice<ExposureMode>(x, UiText.Exposure(x))).ToArray();
        _mode.SelectionChanged += (_, _) =>
        {
            if (_syncing || _mode.SelectedItem is not DisplayChoice<ExposureMode> choice) return;
            _project.Mode = choice.Value;
            SyncModeFields();
            RebuildPreview();
        };
        editor.Children.Add(_mode);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var mode in new[] { ExposureMode.Registration, ExposureMode.Calibration, ExposureMode.ExposureCalibration })
        {
            var chip = Button(UiText.Exposure(mode), () => SelectMode(mode), "chip");
            chip.Margin = new Thickness(0, 2, 5, 2);
            _modeButtons.Add(mode, chip); chips.Children.Add(chip);
        }
        editor.Children.Add(chips);
        editor.Children.Add(Button("Справка по калибровке", async () => await new CalibrationHelpWindow().ShowDialog(this), "link"));
        _calibrationNote = UiTheme.Note(_calibrationHint);
        _stencilNote = UiTheme.Note(_stencilHint);
        _calibrationHint.Classes.Add("caption"); _stencilHint.Classes.Add("caption");
        editor.Children.Add(_calibrationNote); editor.Children.Add(_stencilNote);
        _processCalibrationPanel.Children.Add(UiTheme.Caption("Матрица зигзагов: строки — толщина/зазор, столбцы — компенсация. Каждая проба времени на свежем образце. Для оценки тонких линий увеличьте preview."));
        _processCalibrationPanel.Children.Add(Button("Настроить калибровку", async () => await ConfigureProcessCalibration(), "acc"));
        _processCalibrationNote = UiTheme.Note(_processCalibrationPanel);
        editor.Children.Add(_processCalibrationNote);
        var layers = Section(editor, "Слои Gerber и сверловка");
        _files.ItemTemplate = new FuncDataTemplate<string>((text, _) =>
        {
            var parts = (text ?? "").Split(": ", 2);
            var item = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
            item.Inlines!.Add(new Run(parts[0] + (parts.Length > 1 ? ": " : "")) { Foreground = UiTheme.Brush("#596174") });
            if (parts.Length > 1) item.Inlines.Add(new Run(parts[1]));
            ToolTip.SetTip(item, text); return item;
        });
        layers.Children.Add(_files);
        layers.Children.Add(UiTheme.Caption("Слой Gerber для режима"));
        _layer.PlaceholderText = "Выберите слой Gerber";
        layers.Children.Add(_layer);
        layers.Children.Add(UiTheme.Caption("Контур платы (при необходимости выберите вручную)"));
        layers.Children.Add(_outline);
        layers.Children.Add(UiTheme.Caption("Сверловка для просмотра и CNC"));
        layers.Children.Add(_drill);
        _drill.SelectionChanged += (_, _) =>
        {
            if (_syncing || _package is null || _drill.SelectedItem is not DisplayChoice<string> choice) return;
            if (choice.Value.Length == 0)
            {
                _project.LayerPaths.Remove(GerberLayerKind.Drill);
                _project.LayerNames.Remove(GerberLayerKind.Drill);
            }
            else
            {
                var selected = _package.Layers.Single(l => l.Path == choice.Value);
                _project.LayerPaths[GerberLayerKind.Drill] = selected.Path;
                _project.LayerNames[GerberLayerKind.Drill] = selected.RelativePath;
            }
        };
        _outline.SelectionChanged += (_, _) =>
        {
            if (_syncing || _package is null || _outline.SelectedItem is not DisplayChoice<GerberLayer> choice) return;
            var selected = choice.Value;
            _package.BoardBoundsMm = GerberImportService.MeasureBounds(selected.Path);
            _package.OutlineFallback = false;
            _project.LayerPaths[GerberLayerKind.BoardOutline] = selected.Path;
            _project.LayerNames[GerberLayerKind.BoardOutline] = selected.RelativePath;
            RebuildPreview();
        };
        _layer.SelectionChanged += (_, _) =>
        {
            if (_syncing || _layer.SelectedItem is not DisplayChoice<GerberLayer> choice ||
                _project.CurrentLayerKind == GerberLayerKind.Unknown) return;
            var layer = choice.Value;
            _project.LayerPaths[_project.CurrentLayerKind] = layer.Path;
            _project.LayerNames[_project.CurrentLayerKind] = layer.RelativePath;
            RebuildPreview();
        };
        foreach (var combo in new[] { _mode, _layer, _outline, _drill, _placement, _profile }) UiTheme.Ellipsis(combo);

        var blank = Section(editor, "Профиль заготовки и механические отверстия");
        _profile.PlaceholderText = "Не выбран";
        blank.Children.Add(UiTheme.Caption("Сохранённый профиль"));
        blank.Children.Add(_profile);
        _profile.SelectionChanged += (_, _) =>
        {
            if (_syncing || _profile.SelectedItem is not string name) return;
            var profile = _profiles.Load().FirstOrDefault(x => x.Name == name);
            if (profile is null) return;
            _project.Blank = profile;
            RefreshBlankFields();
            RebuildPreview();
        };
        AddBlankFields(blank);
        var placement = Section(editor, "Положение и размещение плат");
        placement.Children.Add(UiTheme.Caption("мм от левого нижнего угла заготовки"));
        _x = CreateNumberBox("Плата X", () => _project.PcbPositionMm.X,
            v => _project.PcbPositionMm = _project.PcbPositionMm with { X = v });
        _y = CreateNumberBox("Плата Y", () => _project.PcbPositionMm.Y,
            v => _project.PcbPositionMm = _project.PcbPositionMm with { Y = v });
        placement.Children.Add(Pair("Положение платы", _x, _y));
        placement.Children.Add(Button(UiText.Alignment(AlignmentCommand.Center), () => Align(AlignmentCommand.Center)));
        var align = new UniformGrid { Columns = 2, ColumnSpacing = 6, RowSpacing = 4 };
        foreach (var command in Enum.GetValues<AlignmentCommand>().Where(c => c != AlignmentCommand.Center))
            align.Children.Add(Button(UiText.Alignment(command), () => Align(command)));
        placement.Children.Add(align);
        Number(placement, "Отступ выравнивания, мм", () => _edgeInset, v => _edgeInset = v);
        _placement.ItemsSource = Enum.GetValues<CorePlacementMode>()
            .Select(x => new DisplayChoice<CorePlacementMode>(x, UiText.Placement(x))).ToArray();
        _placement.SelectionChanged += (_, _) =>
        {
            if (_syncing || _placement.SelectedItem is not DisplayChoice<CorePlacementMode> choice) return;
            _project.Panelization.Mode = choice.Value;
            RebuildPreview();
        };
        placement.Children.Add(_placement);
        placement.Children.Add(Pair("Интервал, мм",
            CreateNumberBox("Интервал X, мм", () => _project.Panelization.SpacingXmm, v => _project.Panelization.SpacingXmm = v),
            CreateNumberBox("Интервал Y, мм", () => _project.Panelization.SpacingYmm, v => _project.Panelization.SpacingYmm = v)));
        placement.Children.Add(UiTheme.Caption("Поля заготовки, мм"));
        var margins = new UniformGrid { Columns = 4, ColumnSpacing = 6 };
        AddMargin("Слева", "Поле слева, мм", () => _project.Panelization.MarginLeftMm, v => _project.Panelization.MarginLeftMm = v);
        AddMargin("Справа", "Поле справа, мм", () => _project.Panelization.MarginRightMm, v => _project.Panelization.MarginRightMm = v);
        AddMargin("Сверху", "Поле сверху, мм", () => _project.Panelization.MarginTopMm, v => _project.Panelization.MarginTopMm = v);
        AddMargin("Снизу", "Поле снизу, мм", () => _project.Panelization.MarginBottomMm, v => _project.Panelization.MarginBottomMm = v);
        placement.Children.Add(margins);
        var exposure = Section(editor, "Преобразования и экспозиция");
        exposure.Children.Add(UiTheme.Caption("Физический переворот Bottom относительно вертикальной оси центра всей заготовки."));
        var transforms = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var box in new[] { _invert, _mirrorX, _mirrorY })
        {
            box.Margin = new Thickness(0, 0, 8, 0);
            box.IsCheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _project.CurrentTransform.Invert = _invert.IsChecked == true;
                _project.CurrentTransform.MirrorX = _mirrorX.IsChecked == true;
                _project.CurrentTransform.MirrorY = _mirrorY.IsChecked == true;
                RebuildPreview();
            };
            transforms.Children.Add(box);
        }
        exposure.Children.Add(transforms);
        _time = Number(exposure, "Время экспозиции, с", () => _project.CurrentExposureSeconds, SetExposureTime);
        ((Grid)_time.Parent!).Children.OfType<TextBlock>().First().FontWeight = FontWeight.SemiBold;
        _compensation = Number(exposure, "Компенсация экспозиции, мм", () => _project.CurrentCompensationMm, SetCompensation);
        exposure.Children.Add(UiTheme.Caption("Смещение края белой области: «+» расширяет её, «−» сужает."));
        exposure.Children.Add(new Expander { Header = "Подробнее", Classes = { "inline" }, Content = UiTheme.Caption("Например, +0,025 мм увеличивает ширину белой линии примерно на 0,05 мм; тёмные окна сужаются. Значение округляется по шагу пикселя X/Y. Время засветки не меняется; поправку подбирайте по калибровке.") });
        _pwm = new TextBox { PlaceholderText = "из шаблона", Classes = { "num" } };
        ToolTip.SetTip(_pwm, "пусто = значение из шаблона");
        exposure.Children.Add(Row("PWM подсветки (необязательно, 1..255)", _pwm));
        _pwm.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_pwm.Text)) _project.Exposure.LightPwm = null;
            else if (byte.TryParse(_pwm.Text, out var value) && value > 0) _project.Exposure.LightPwm = value;
            else ShowError(new InvalidOperationException("PWM должен быть от 1 до 255."));
            _exposureProfiles.Save(_project.Exposure);
            UpdateSummary();
        };
        _aa.IsCheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            _project.AntiAliasing = _aa.IsChecked == true;
            RebuildPreview();
        };
        _aa.Content = new TextBlock { Text = "Сглаживание (дополнительно; по умолчанию выключено)", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        exposure.Children.Add(_aa);

        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(12), RowSpacing = 10, ClipToBounds = true };
        Grid.SetRow(center, 1); Grid.SetColumn(center, 1); root.Children.Add(center);
        var previewHeader = new WrapPanel { Orientation = Orientation.Horizontal };
        previewHeader.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 0), Children =
        {
            UiTheme.Heading("Предпросмотр экспозиции"),
            UiTheme.Caption("□ Белое пропускает UV   ■ Чёрное блокирует UV")
        } });
        previewHeader.Children.Add(Button("Обновить предпросмотр в разрешении LCD", RebuildPreview));
        center.Children.Add(previewHeader);
        var viewport = new Grid();
        viewport.Children.Add(_preview); viewport.Children.Add(_previewProgress);
        var empty = new StackPanel { Spacing = 12, MaxWidth = 330 };
        empty.Children.Add(UiTheme.Heading("Плата не загружена"));
        empty.Children.Add(UiTheme.Caption("Откройте папку или ZIP с Gerber. Для калибровки и точек центровки плата не требуется."));
        empty.Children.Add(Button("Открыть папку Gerber", async () => await OpenFolder(), "acc"));
        empty.Children.Add(Button("Открыть ZIP с Gerber", async () => await OpenZip()));
        _emptyPreview = new Border { Child = empty, Classes = { "panel" }, Padding = new Thickness(24), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        viewport.Children.Add(_emptyPreview);
        var previewBorder = new Border { Child = viewport, BorderBrush = UiTheme.Brush("#C9CED8"), BorderThickness = new Thickness(1), Background = UiTheme.Brush("#D9DDE4"), Padding = new Thickness(12) };
        Grid.SetRow(previewBorder, 1); center.Children.Add(previewBorder);
        var hint = new StackPanel { Spacing = 6 };
        hint.Children.Add(UiTheme.Caption("Колесо: масштаб; правая кнопка: сдвиг; левая кнопка: перемещение платы в режиме «Одна плата»."));
        var overlays = new WrapPanel { Orientation = Orientation.Horizontal };
        AddLegend("Заготовка", Brushes.DeepSkyBlue, 2);
        AddLegend("Платы", Brushes.LimeGreen, 1);
        AddLegend("Механические отверстия", Brushes.OrangeRed, 1);
        hint.Children.Add(overlays); Grid.SetRow(hint, 2); center.Children.Add(hint);

        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var export = new StackPanel { Spacing = 6, Margin = new Thickness(14) };
        export.Children.Add(UiTheme.Heading("Экспорт"));
        export.Children.Add(Button("Экспортировать CXDLPV4", async () => await Export(), "primary"));
        const string pngHint = "PNG: полное разрешение LCD, ориентация предпросмотра, физический масштаб X/Y. Печать: 100 %, без подгонки. Для лазера проверьте размеры и полярность в его программе.";
        var png = Button("Экспортировать растр в PNG", async () => await ExportRaster());
        ToolTip.SetTip(png, pngHint); export.Children.Add(png);
        _seriesButton = Button("Экспортировать пробы с разным временем", async () => await ExportCalibrationSeries());
        export.Children.Add(Button("Экспорт контуров в DXF", async () => await ExportDxf()));
        export.Children.Add(Button("Сверловка и раскладка для CNC", async () => await ExportDxf(true)));
        export.Children.Add(_seriesButton);
        export.Children.Add(new TextBlock { Text = pngHint, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = UiTheme.Brush("#596174") });
        right.Children.Add(export);
        var info = new StackPanel { Spacing = 10, Margin = new Thickness(14, 0, 14, 14) };
        info.Children.Add(UiTheme.Heading("Состояние и предупреждения"));
        _statusBorder.Child = _status; info.Children.Add(_statusBorder);
        info.Children.Add(_warnings); info.Children.Add(_summary);
        var infoScroll = new ScrollViewer { Content = info, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(infoScroll, 1); right.Children.Add(infoScroll);
        var rightBorder = new Border { Child = right, Classes = { "panel" } };
        Grid.SetColumn(rightBorder, 2); Grid.SetRow(rightBorder, 1); root.Children.Add(rightBorder);
        var statusBar = new Border { Child = _statusBar, Background = UiTheme.Brush("#F7F8FA"), Padding = new Thickness(14, 4), BorderBrush = UiTheme.Brush("#DDE1E8"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(statusBar, 2); Grid.SetColumnSpan(statusBar, 3); root.Children.Add(statusBar);
        UiTheme.FitToScreen(this);
        void AddMargin(string title, string label, Func<double> read, Action<double> write)
        {
            var field = new StackPanel { Spacing = 4 };
            field.Children.Add(UiTheme.Caption(title)); field.Children.Add(CreateNumberBox(label, read, write));
            margins.Children.Add(field);
        }
        void AddLegend(string title, IBrush color, double thickness)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 12, 0) };
            item.Children.Add(new Border { Width = 16, Height = thickness, Background = color, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(UiTheme.Caption(title)); overlays.Children.Add(item);
        }

        _preview.PositionChanged = (position, commit) =>
        {
            _x.Text = UiText.Number(position.X);
            _y.Text = UiText.Number(position.Y);
            if (commit) RebuildPreview();
        };
        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await BuildPreviewAsync(); };
        Closed += (_, _) =>
        {
            _closed = true; _previewGeneration++; _previewTimer.Stop();
            _preview.ClearPreview(); _package?.Dispose();
        };
        _project.Exposure = _exposureProfiles.Load();
        _project.TemplatePath = ApplicationPaths.DefaultTemplate;
        if (File.Exists(_project.TemplatePath)) LoadTemplate(_project.TemplatePath);
        RefreshProfileNames();
        SyncModeFields();
        RebuildPreview();
    }

    private double _edgeInset;
    private readonly List<TextBox> _blankFields = [];
    private TextBox _profileName = null!;

    private void AddBlankFields(StackPanel panel)
    {
        _profileName = new TextBox { Text = _project.Blank.Name };
        _profileName.LostFocus += (_, _) => _project.Blank.Name = _profileName.Text ?? "";
        panel.Children.Add(Row("Название профиля", _profileName));
        panel.Children.Add(Button("Сохранить профиль заготовки", SaveBlankProfile));
        panel.Children.Add(UiTheme.Note(UiTheme.Caption("Отступы отверстий и точек центровки 10 мм приведены как пример. Перед экспозицией задайте размеры своей оснастки."), "warn"));
        var width = Blank("Ширина заготовки, мм", () => _project.Blank.WidthMm, v => _project.Blank.WidthMm = v);
        var height = Blank("Высота заготовки, мм", () => _project.Blank.HeightMm, v => _project.Blank.HeightMm = v);
        panel.Children.Add(Pair("Заготовка, мм", width, height, "Ш", "В"));
        panel.Children.Add(Row("Диаметр механических отверстий, мм", Blank("Диаметр механических отверстий, мм", () => _project.Blank.RegistrationHoleDiameterMm, v => _project.Blank.RegistrationHoleDiameterMm = v)));
        var holeX = Blank("Отступ отверстий X, мм", () => _project.Blank.HoleInsetXmm, v => _project.Blank.HoleInsetXmm = v);
        var holeY = Blank("Отступ отверстий Y, мм", () => _project.Blank.HoleInsetYmm, v => _project.Blank.HoleInsetYmm = v);
        panel.Children.Add(Pair("Отступ отверстий, мм", holeX, holeY));
        panel.Children.Add(Row("Запретная зона вокруг отверстий, мм", Blank("Запретная зона вокруг отверстий, мм", () => _project.Blank.HoleClearanceMm, v => _project.Blank.HoleClearanceMm = v)));
        panel.Children.Add(Row("Толщина служебных линий, мм", Blank("Толщина служебных линий, мм", () => _project.Blank.ServiceLineThicknessMm, v => _project.Blank.ServiceLineThicknessMm = v)));
        var pointX = Blank("Отступ точек центровки X, мм", () => _project.Blank.AlignmentInsetXmm, v => _project.Blank.AlignmentInsetXmm = v);
        var pointY = Blank("Отступ точек центровки Y, мм", () => _project.Blank.AlignmentInsetYmm, v => _project.Blank.AlignmentInsetYmm = v);
        panel.Children.Add(Pair("Отступ точек центровки, мм", pointX, pointY));
        TextBox Blank(string label, Func<double> read, Action<double> write)
        {
            var box = CreateNumberBox(label, read, write); _blankFields.Add(box); return box;
        }
    }

    private static StackPanel Section(StackPanel parent, string title)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 12) };
        parent.Children.Add(new Expander { Header = UiTheme.Heading(title), IsExpanded = true, Content = panel });
        return panel;
    }

    private static Button Button(string label, Action action, string? cls = null)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap } };
        if (cls is not null) button.Classes.Add(cls);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Toolbar(string label, string path, Action action)
    {
        var button = Button(label, action, "toolbar");
        button.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
        {
            new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Width = 16, Height = 16,
                Stroke = UiTheme.Brush("#596174"), StrokeThickness = 1.4, Stretch = Stretch.Uniform },
            new TextBlock { Text = label }
        } };
        return button;
    }

    private static Grid Row(string label, Control field)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,92"), ColumnSpacing = 8 };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(field, 1); row.Children.Add(field); return row;
    }

    private static Grid Pair(string label, Control x, Control y, string xLabel = "X", string yLabel = "Y")
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,62,12,62"), ColumnSpacing = 4 };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        var lx = UiTheme.Caption(xLabel); var ly = UiTheme.Caption(yLabel);
        lx.VerticalAlignment = ly.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lx, 1); Grid.SetColumn(x, 2); Grid.SetColumn(ly, 3); Grid.SetColumn(y, 4);
        row.Children.Add(lx); row.Children.Add(x); row.Children.Add(ly); row.Children.Add(y); return row;
    }

    private TextBox Number(StackPanel panel, string label, Func<double> read, Action<double> write)
    {
        var box = CreateNumberBox(label, read, write); panel.Children.Add(Row(label, box)); return box;
    }

    private TextBox CreateNumberBox(string label, Func<double> read, Action<double> write)
    {
        var box = new TextBox { Text = UiText.Number(read()), Classes = { "num" } };
        ToolTip.SetTip(box, label);
        box.LostFocus += (_, _) =>
        {
            if (_syncing) return;
            if (!UiText.TryNumber(box.Text, out var value))
            {
                ShowError(new InvalidOperationException($"{label}: введите число."));
                box.Text = UiText.Number(read());
                return;
            }
            write(value);
            RebuildPreview();
        };
        return box;
    }

    private void RefreshBlankFields()
    {
        _profileName.Text = _project.Blank.Name;
        var values = new[] { _project.Blank.WidthMm, _project.Blank.HeightMm,
            _project.Blank.RegistrationHoleDiameterMm, _project.Blank.HoleInsetXmm,
            _project.Blank.HoleInsetYmm, _project.Blank.HoleClearanceMm,
            _project.Blank.ServiceLineThicknessMm, _project.Blank.AlignmentInsetXmm,
            _project.Blank.AlignmentInsetYmm };
        for (var i = 0; i < Math.Min(values.Length, _blankFields.Count); i++)
            _blankFields[i].Text = UiText.Number(values[i]);
    }

    private void SetExposureTime(double value)
    {
        switch (_project.Mode)
        {
            case ExposureMode.TopCopper or ExposureMode.BottomCopper: _project.Exposure.CopperSeconds = value; break;
            case ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask: _project.Exposure.SolderMaskSeconds = value; break;
            case ExposureMode.TopStencil or ExposureMode.BottomStencil: _project.Exposure.StencilSeconds = value; break;
            case ExposureMode.Registration: _project.Exposure.RegistrationSeconds = value; break;
            case ExposureMode.Calibration: _project.Exposure.CalibrationSeconds = value; break;
            case ExposureMode.ExposureCalibration: _project.Exposure.ProcessCalibrationSeconds = value; break;
        }
        _exposureProfiles.Save(_project.Exposure);
    }

    private void SetCompensation(double value)
    {
        if (_project.Mode is ExposureMode.TopCopper or ExposureMode.BottomCopper)
            _project.Exposure.CopperCompensationMm = value;
        else if (_project.Mode is ExposureMode.TopSolderMask or ExposureMode.BottomSolderMask)
            _project.Exposure.SolderMaskCompensationMm = value;
        else if (_project.IsStencil)
            _project.Exposure.StencilCompensationMm = value;
        _exposureProfiles.Save(_project.Exposure);
    }

    private void SelectMode(ExposureMode mode) => _mode.SelectedItem =
        _mode.ItemsSource?.Cast<DisplayChoice<ExposureMode>>().FirstOrDefault(x => x.Value == mode);

    private void SyncModeFields()
    {
        _syncing = true;
        _mode.SelectedItem = _mode.ItemsSource?.Cast<DisplayChoice<ExposureMode>>()
            .FirstOrDefault(x => x.Value == _project.Mode);
        _calibrationNote.IsVisible = _calibrationHint.IsVisible = _project.Mode == ExposureMode.Calibration;
        _stencilNote.IsVisible = _stencilHint.IsVisible = _project.IsStencil;
        foreach (var (mode, button) in _modeButtons) button.Classes.Set("on", _project.Mode == mode);
        _seriesButton.Classes.Set("acc", _project.Mode == ExposureMode.ExposureCalibration);
        _invert.IsEnabled = !_project.IsStencil;
        var processCalibration = _project.Mode == ExposureMode.ExposureCalibration;
        _processCalibrationNote.IsVisible = _processCalibrationPanel.IsVisible = processCalibration;
        _compensation.IsEnabled = !processCalibration;
        _mirrorX.IsEnabled = _mirrorY.IsEnabled = _aa.IsEnabled = !processCalibration;
        _placement.SelectedItem = _placement.ItemsSource?.Cast<DisplayChoice<CorePlacementMode>>()
            .FirstOrDefault(x => x.Value == _project.Panelization.Mode);
        _invert.IsChecked = _project.IsStencil || _project.CurrentTransform.Invert;
        _mirrorX.IsChecked = !processCalibration && _project.CurrentTransform.MirrorX;
        _mirrorY.IsChecked = !processCalibration && _project.CurrentTransform.MirrorY;
        _aa.IsChecked = !processCalibration && _project.AntiAliasing;
        _x.Text = UiText.Number(_project.PcbPositionMm.X);
        _y.Text = UiText.Number(_project.PcbPositionMm.Y);
        _time.Text = UiText.Number(_project.CurrentExposureSeconds);
        _compensation.Text = UiText.Number(_project.CurrentCompensationMm);
        _pwm.Text = _project.Exposure.LightPwm?.ToString() ?? "";
        _layer.ItemsSource = _package?.Layers.Where(x => x.Kind is not (GerberLayerKind.Drill or GerberLayerKind.BoardOutline))
            .Select(x => new DisplayChoice<GerberLayer>(x, $"{UiText.Layer(x.Kind)}: {x.Name}")).ToArray() ?? [];
        _outline.ItemsSource = _package?.Layers.Where(x => x.Kind != GerberLayerKind.Drill)
            .Select(x => new DisplayChoice<GerberLayer>(x, $"{UiText.Layer(x.Kind)}: {x.Name}")).ToArray() ?? [];
        if (_package is not null && _project.LayerPaths.TryGetValue(GerberLayerKind.BoardOutline, out var outlinePath))
            _outline.SelectedItem = _outline.ItemsSource?.Cast<DisplayChoice<GerberLayer>>()
                .FirstOrDefault(x => x.Value.Path == outlinePath);
        if (_package is not null && _project.LayerPaths.TryGetValue(_project.CurrentLayerKind, out var path))
            _layer.SelectedItem = _layer.ItemsSource?.Cast<DisplayChoice<GerberLayer>>()
                .FirstOrDefault(x => x.Value.Path == path);
        var drillLayers = _package?.Layers.Where(l => l.Kind == GerberLayerKind.Drill).ToArray() ?? [];
        _drill.ItemsSource = new[] { new DisplayChoice<string>("", $"Все файлы сверловки ({drillLayers.Length})") }
            .Concat(drillLayers.Select(l => new DisplayChoice<string>(l.Path, l.RelativePath))).ToArray();
        var drillPath = _project.LayerPaths.GetValueOrDefault(GerberLayerKind.Drill, "");
        _drill.SelectedItem = _drill.ItemsSource.Cast<DisplayChoice<string>>().FirstOrDefault(c => c.Value == drillPath);
        _syncing = false;
        UpdateSummary();
    }

    private void LoadTemplate(string path)
    {
        try
        {
            _printer = _template.ReadInfo(path);
            _project.TemplatePath = path;
            _log.Write($"Template={path}; resolution={_printer.ResolutionX}x{_printer.ResolutionY}; display={_printer.DisplayWidthMm}x{_printer.DisplayHeightMm}mm; pitch={_printer.PixelPitchXmm:F6},{_printer.PixelPitchYmm:F6}mm");
            SetStatus($"Шаблон загружен: {Path.GetFileName(path)}");
            RebuildPreview();
        }
        catch (Exception error) { ShowError(error); }
    }

    private bool Import(string path)
    {
        try
        {
            var package = _importer.Import(path);
            _package?.Dispose();
            _package = package;
            _project.GerberSourcePath = path;
            _project.Name = package.ProjectName;
            _project.LayerPaths.Clear();
            _project.LayerNames.Clear();
            var outlines = package.Layers.Where(x => x.Kind == GerberLayerKind.BoardOutline).ToArray();
            if (outlines.Length == 1)
            {
                _project.LayerPaths[GerberLayerKind.BoardOutline] = outlines[0].Path;
                _project.LayerNames[GerberLayerKind.BoardOutline] = outlines[0].RelativePath;
            }
            foreach (var kind in new[] { GerberLayerKind.TopCopper, GerberLayerKind.BottomCopper,
                         GerberLayerKind.TopSolderMask, GerberLayerKind.BottomSolderMask,
                         GerberLayerKind.TopPasteMask, GerberLayerKind.BottomPasteMask })
            {
                var matching = package.Layers.Where(x => x.Kind == kind).ToArray();
                if (matching.Length != 1) continue;
                _project.LayerPaths[kind] = matching[0].Path;
                _project.LayerNames[kind] = matching[0].RelativePath;
            }
            _files.ItemsSource = package.Layers.Select(x => $"{UiText.Layer(x.Kind)}: {x.Name}").ToArray();
            _log.Write($"Import={path}; files={string.Join(',', package.Layers.Select(x => x.Name))}; boardBounds={package.BoardBoundsMm}; fallback={package.OutlineFallback}");
            var ambiguous = package.Layers.GroupBy(x => x.Kind)
                .Where(g => g.Key is not (GerberLayerKind.Drill or GerberLayerKind.Other) && g.Count() > 1)
                .Select(g => UiText.Layer(g.Key)).ToArray();
            _importWarnings =
            [
                .. package.OutlineFallback ? new[] { UiText.OutlineWarning } : [],
                .. ambiguous.Length > 0 ? new[] { $"Требуется ручной выбор: {string.Join(", ", ambiguous)}." } : []
            ];
            var importStatus = package.OutlineFallback ? "ВНИМАНИЕ: контур платы отсутствует, габариты оценены по artwork." :
                ambiguous.Length > 0 ? $"Требуется ручной выбор: {string.Join(", ", ambiguous)}." :
                $"Импортировано {package.Layers.Count} файлов.";
            SyncModeFields();
            RebuildPreview();
            SetStatus(importStatus, package.OutlineFallback || ambiguous.Length > 0 ? "warn" : null);
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }

    private void RebuildPreview()
    {
        _previewGeneration++;
        _previewTimer.Stop();
        if (_closed) return;
        _emptyPreview.IsVisible = _package is null && _project.CurrentLayerKind != GerberLayerKind.Unknown;
        if (_printer is null) { SetStatus("Загрузите шаблон CXDLPV4."); UpdateSummary(); return; }
        if (_package is null && _project.Mode is not (ExposureMode.Registration or ExposureMode.Calibration or ExposureMode.ExposureCalibration))
        {
            _preview.ClearPreview();
            _previewProgress.IsVisible = false;
            _boardCount = 0;
            _positions = [];
            SetStatus("Импортируйте Gerber, чтобы показать экспозицию платы.");
            UpdateSummary();
            return;
        }
        try
        {
            _positions = _project.CurrentLayerKind != GerberLayerKind.Unknown && _package?.BoardBoundsMm is { } bounds
                ? new PanelizationService(_layout).LayoutPhysical(_project, bounds.Width, bounds.Height) : [];
            _boardCount = _positions.Count;
            UpdateSummary();
        }
        catch (Exception error)
        {
            _preview.ClearPreview(); _previewProgress.IsVisible = false;
            _positions = []; _boardCount = 0; ShowError(error); return;
        }
        _previewProgress.IsVisible = true;
        SetStatus("Построение предпросмотра в разрешении LCD…");
        _previewTimer.Start();
    }

    private async Task BuildPreviewAsync()
    {
        var generation = _previewGeneration;
        if (_closed || _printer is not { } printer) return;
        // Capture mutable settings on the UI thread; background rendering never reads the active project.
        var project = JsonSerializer.Deserialize<ProjectModel>(JsonSerializer.Serialize(_project, LocalStorage.JsonOptions), LocalStorage.JsonOptions)!;
        project.LayerPaths = new Dictionary<GerberLayerKind, string>(_project.LayerPaths);
        var bounds = _package?.BoardBoundsMm;
        await _previewBuild.WaitAsync();
        try
        {
            if (_closed || generation != _previewGeneration) return;
            var frame = await Task.Run(() =>
            {
                using var result = _raster.Build(project, printer, RasterGeometry.Native(printer), bounds);
                var bitmap = new Bitmap(Avalonia.Platform.PixelFormats.Gray8, Avalonia.Platform.AlphaFormat.Opaque,
                    result.Image.DataPointer, new PixelSize(result.Image.Width, result.Image.Height), new Vector(96, 96), result.Image.Step);
                return (Bitmap: bitmap, Blank: result.BlankOnLcd, Boards: result.Boards.ToArray());
            });
            if (_closed || generation != _previewGeneration) { frame.Bitmap.Dispose(); return; }
            _preview.SetPreview(frame.Bitmap, printer, _project, frame.Blank, frame.Boards);
            _previewProgress.IsVisible = false;
            _boardCount = frame.Boards.Length;
            _positions = frame.Boards;
            SetStatus($"Предпросмотр: {printer.ResolutionX} × {printer.ResolutionY} px (разрешение LCD). Белое пропускает UV.");
        }
        catch (Exception error)
        {
            if (_closed || generation != _previewGeneration) return;
            _preview.ClearPreview();
            _previewProgress.IsVisible = false;
            _boardCount = 0;
            _positions = [];
            ShowError(error);
        }
        finally { _previewBuild.Release(); }
        if (_closed || generation != _previewGeneration) return;
        UpdateSummary();
    }

    private void SetStatus(string text, string? kind = null)
    {
        _status.Text = text;
        foreach (var cls in new[] { "warn", "err", "ok" }) _statusBorder.Classes.Set(cls, cls == kind);
        _status.Foreground = UiTheme.Brush(kind switch { "err" => "#8E1A12", "warn" => "#5E3B00", "ok" => "#17532F", _ => "#2A3B5C" });
    }

    private void UpdateSummary()
    {
        _statusBar.Text = $"PCB Expo {AppVersion.Current} | HALOT-MAGE S  •  {UiText.Exposure(_project.Mode)}  •  Плат: {_boardCount}";
        _summary.Children.Clear(); _warnings.Children.Clear();
        if (_printer is null)
        {
            foreach (var warning in _importWarnings) _warnings.Children.Add(UiTheme.Note(UiTheme.Caption(warning), "warn"));
            _summary.Children.Add(UiTheme.Note(UiTheme.Caption("Загрузите шаблон CXDLPV4.")));
            return;
        }
        var parts = UiText.SummaryParts(_printer, _project, _package, _boardCount, _positions);
        foreach (var warning in parts.Warnings.Concat(_importWarnings).Distinct(StringComparer.Ordinal))
            _warnings.Children.Add(UiTheme.Note(UiTheme.Caption(warning), "warn"));
        _summary.Children.Add(UiTheme.SummaryView(parts, warnings: false));
    }

    private void ShowError(Exception error)
    {
        SetStatus(error.Message, "err");
        _log.Error(error);
        UpdateSummary();
    }

    private void Align(AlignmentCommand command)
    {
        if (_package?.BoardBoundsMm is not { } bounds) return;
        var x = _project.PcbPositionMm.X;
        var y = _project.PcbPositionMm.Y;
        switch (command)
        {
            case AlignmentCommand.Center: x = (_project.Blank.WidthMm - bounds.Width) / 2; y = (_project.Blank.HeightMm - bounds.Height) / 2; break;
            case AlignmentCommand.Left: x = _edgeInset; break;
            case AlignmentCommand.Right: x = _project.Blank.WidthMm - bounds.Width - _edgeInset; break;
            case AlignmentCommand.Top: y = _project.Blank.HeightMm - bounds.Height - _edgeInset; break;
            case AlignmentCommand.Bottom: y = _edgeInset; break;
        }
        _project.PcbPositionMm = new PointMm(x, y);
        _x.Text = UiText.Number(x);
        _y.Text = UiText.Number(y);
        RebuildPreview();
    }

    private void RefreshProfileNames()
    {
        _syncing = true;
        _profile.ItemsSource = _profiles.Load().Select(x => x.Name).ToArray();
        _syncing = false;
    }

    private void SaveBlankProfile()
    {
        try
        {
            _profiles.Save(_project.Blank);
            RefreshProfileNames();
            SetStatus($"Профиль {_project.Blank.Name} сохранён локально.");
        }
        catch (Exception error) { ShowError(error); }
    }

    private async Task OpenFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Открыть папку Gerber" });
        if (folders.Count > 0) Import(folders[0].Path.LocalPath);
    }

    private async Task OpenZip()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть ZIP с Gerber", FileTypeFilter = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }]
        });
        if (files.Count > 0) Import(files[0].Path.LocalPath);
    }

    private async Task OpenTemplate()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Загрузить шаблон CXDLPV4", FileTypeFilter = [new FilePickerFileType("CXDLPV4") { Patterns = ["*.cxdlpv4"] }]
        });
        if (files.Count > 0) LoadTemplate(files[0].Path.LocalPath);
    }

    private async Task OpenProject()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть проект PCB Expo", FileTypeFilter = [new FilePickerFileType("PCB Expo") { Patterns = ["*.pcbexpo.json"] }]
        });
        if (files.Count == 0) return;
        try
        {
            var loaded = _projects.Load(files[0].Path.LocalPath);
            var printer = _template.ReadInfo(loaded.TemplatePath);
            _project = loaded;
            _printer = printer;
            var savedName = loaded.Name;
            if (!string.IsNullOrWhiteSpace(_project.GerberSourcePath))
            {
                var savedNames = _project.LayerNames.ToDictionary();
                if (!Import(_project.GerberSourcePath)) return;
                LayerAssignments.Restore(_project, _package!, savedNames);
            }
            _project.Name = savedName;
            RefreshBlankFields();
            SyncModeFields();
            RebuildPreview();
        }
        catch (Exception error) { ShowError(error); }
    }

    private async Task SaveProject()
    {
        var path = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить проект PCB Expo", SuggestedFileName = $"{UiText.SafeFileName(_project.Name)}.pcbexpo.json",
            FileTypeChoices = [new FilePickerFileType("PCB Expo") { Patterns = ["*.pcbexpo.json"] }]
        });
        if (path is null) return;
        try
        {
            _projects.Save(_project, path.Path.LocalPath);
            SetStatus($"Проект сохранён: {path.Path.LocalPath}");
        }
        catch (Exception error) { ShowError(error); }
    }

    private async Task Export()
    {
        if (_printer is null) { ShowError(new InvalidOperationException("Сначала загрузите шаблон.")); return; }
        if (_project.CurrentExposureSeconds <= 0)
        {
            ShowError(new InvalidOperationException("Укажите время экспонирования; значение шаблона не используется автоматически."));
            return;
        }
        if (!await ConfirmExport()) return;
        var blankName = $"{_project.Blank.WidthMm:0.#}x{_project.Blank.HeightMm:0.#}";
        var filename = $"{UiText.SafeFileName(_project.Name)}_{_project.Mode}_{blankName}.cxdlpv4";
        var path = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспортировать CXDLPV4", SuggestedFileName = filename,
            FileTypeChoices = [new FilePickerFileType("CXDLPV4") { Patterns = ["*.cxdlpv4"] }]
        });
        if (path is null) return;
        try
        {
            using var result = _raster.Build(_project, _printer, RasterGeometry.Native(_printer), _package?.BoardBoundsMm);
            var output = _template.ExportAndVerify(_project.TemplatePath, path.Path.LocalPath,
                result.Image, _project.CurrentExposureSeconds, _project.Exposure.LightPwm);
            _log.Write($"Export={output.Path}; mode={_project.Mode}; blank={_project.Blank.WidthMm}x{_project.Blank.HeightMm}; boards={result.Boards.Count}; positions={string.Join(';', result.Boards)}; mirror={_project.CurrentTransform.MirrorX},{_project.CurrentTransform.MirrorY}; invert={_project.IsStencil || _project.CurrentTransform.Invert}; exposure={_project.CurrentExposureSeconds}; compensation={_project.CurrentCompensationMm}; diffPixels={output.DifferentPixels}");
            SetStatus($"Экспорт и повторное чтение успешны: {output.Path}", "ok");
        }
        catch (Exception error) { ShowError(error); }
    }

    private async Task ExportRaster()
    {
        if (_printer is not { } printer) { ShowError(new InvalidOperationException("Сначала загрузите шаблон для разрешения и физического размера растра.")); return; }
        var path = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспортировать растр в полном разрешении", DefaultExtension = "png",
            SuggestedFileName = $"{UiText.SafeFileName(_project.Name)}_{_project.Mode}.png",
            FileTypeChoices = [new FilePickerFileType("PNG без потерь") { Patterns = ["*.png"] }]
        });
        if (path is null) return;
        var wasEnabled = ((Control)Content!).IsEnabled;
        ((Control)Content!).IsEnabled = false;
        try
        {
            var outputPath = path.Path.LocalPath;
            var protectedPaths = (_package?.Layers.Select(l => l.Path) ?? [])
                .Concat([_project.TemplatePath, _project.GerberSourcePath]);
            if (protectedPaths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Any(p => Path.GetFullPath(p).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Нельзя перезаписывать исходные файлы.");
            if (!Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выберите имя с расширением .png.");
            var raster = RasterGeometry.Native(printer);
            var project = JsonSerializer.Deserialize<ProjectModel>(JsonSerializer.Serialize(_project, LocalStorage.JsonOptions), LocalStorage.JsonOptions)!;
            project.LayerPaths = new Dictionary<GerberLayerKind, string>(_project.LayerPaths);
            var bounds = _package?.BoardBoundsMm;
            SetStatus("Экспорт растра в полном разрешении…");
            await Task.Run(() =>
            {
                using var result = _raster.Build(project, printer, raster, bounds);
                new RasterImageExportService().ExportPng(outputPath, result.Image, raster);
            });
            SetStatus($"PNG сохранён: {outputPath}\n{raster.WidthPx} × {raster.HeightPx} px; {UiText.Number(raster.DisplayWidthMm)} × {UiText.Number(raster.DisplayHeightMm)} мм. Ориентация предпросмотра; печать 100 %, без подгонки.", "ok");
            _log.Write($"RasterExport={outputPath}; mode={project.Mode}; resolution={raster.WidthPx}x{raster.HeightPx}; sizeMm={raster.DisplayWidthMm}x{raster.DisplayHeightMm}");
        }
        catch (Exception error) { ShowError(error); }
        finally { ((Control)Content!).IsEnabled = wasEnabled; }
    }

    private async Task ConfigureProcessCalibration()
    {
        await new ExposureCalibrationWindow(_project, _printer, (settings, time, target) =>
        {
            _project.ProcessCalibration = settings;
            _project.Exposure.ProcessCalibrationSeconds = time;
            if (target == CalibrationApplyTarget.Copper)
            {
                _project.Exposure.CopperSeconds = time;
                _project.Exposure.CopperCompensationMm = settings.SelectedCompensationMm;
            }
            else if (target == CalibrationApplyTarget.SolderMask)
            {
                _project.Exposure.SolderMaskSeconds = time;
                _project.Exposure.SolderMaskCompensationMm = settings.SelectedCompensationMm;
            }
            _exposureProfiles.Save(_project.Exposure);
            SyncModeFields(); RebuildPreview();
            if (target is not null) SetStatus($"Результат калибровки применён к {(target == CalibrationApplyTarget.Copper ? "меди" : "паяльной маске")}: {UiText.Number(time)} с, {UiText.Number(settings.SelectedCompensationMm)} мм.");
        }).ShowDialog(this);
    }

    private async Task ExportCalibrationSeries()
    {
        if (_exportingCalibration) return;
        SelectMode(ExposureMode.ExposureCalibration);
        if (_printer is null) { ShowError(new InvalidOperationException("Сначала загрузите шаблон принтера.")); return; }
        try { _project.ProcessCalibration.Validate(requireTimes: true); }
        catch (Exception error)
        {
            ShowError(error);
            await ConfigureProcessCalibration();
            try { _project.ProcessCalibration.Validate(requireTimes: true); }
            catch (Exception configurationError) { ShowError(configurationError); return; }
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Папка для серии калибровки (будет создана отдельная подпапка)" });
        if (folders.Count == 0) return;
        _exportingCalibration = true;
        var wasEnabled = ((Control)Content!).IsEnabled;
        ((Control)Content!).IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => SetStatus(message));
            var result = await Task.Run(() => new ExposureCalibrationExportService().Export(_project, _printer,
                folders[0].Path.LocalPath, progress));
            SetStatus($"Серия калибровки сохранена: {result.Directory}. Файлов: {result.Files.Count}; повторное чтение каждого успешно. Каждый опыт — на свежем образце.", "ok");
            _log.Write($"ExposureCalibrationExport={result.Directory}; files={result.Files.Count}");
        }
        catch (Exception error) { ShowError(error); }
        finally { _exportingCalibration = false; ((Control)Content!).IsEnabled = wasEnabled; }
    }

    private async Task ExportDxf(bool cncFirst = false)
    {
        if (cncFirst && _package is null) { ShowError(new InvalidOperationException("Сначала импортируйте Gerber и сверловку .drl/.xln.")); return; }
        var sources = new DxfSourceCatalog(_raster).Create(_project, _package, _printer, cncFirst);
        var protectedPaths = (_package?.Layers.Select(l => l.Path) ?? [])
            .Concat([_project.TemplatePath, _project.GerberSourcePath]);
        var dialog = new DxfExportWindow(_project.Name, sources, protectedPaths)
        {
            Exported = (path, count) =>
            {
                SetStatus($"DXF сохранён: {path}. Контуров: {count}; единицы — мм.", "ok");
                _log.Write($"DxfExport={path}; contours={count}; units=mm");
            }
        };
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmExport()
    {
        var dialog = new Window { Title = "Параметры экспорта", Width = 570, Height = 720, MinWidth = 480, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.FitToScreen(dialog);
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 16 };
        content.Children.Add(new TextBlock { Text = "Проверьте параметры экспозиции", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = UiTheme.Brush("#10173A") });
        content.Children.Add(UiTheme.SummaryView(UiText.SummaryParts(_printer!, _project, _package, _boardCount, _positions)));
        foreach (var warning in _importWarnings) content.Children.Add(UiTheme.Note(UiTheme.Caption(warning), "warn"));
        root.Children.Add(new ScrollViewer { Content = content });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        buttons.Children.Add(Button("Отмена", () => dialog.Close(false)));
        buttons.Children.Add(Button("Экспортировать", () => dialog.Close(true), "primary"));
        var footer = UiTheme.Footer(buttons); Grid.SetRow(footer, 1); root.Children.Add(footer);
        dialog.Content = root;
        return await dialog.ShowDialog<bool>(this);
    }

}
