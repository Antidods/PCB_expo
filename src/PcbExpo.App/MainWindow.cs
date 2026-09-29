using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Emgu.CV;
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
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _files = new() { Height = 145 };
    private readonly ComboBox _mode = new();
    private readonly ComboBox _layer = new();
    private readonly ComboBox _outline = new();
    private readonly ComboBox _placement = new();
    private readonly ComboBox _profile = new();
    private readonly TextBlock _calibrationHint = new()
    {
        Text = "Эталон: линия 100 мм и квадрат 50 × 50 мм. Минимальная заготовка 110 × 70 мм. Порядок измерений — в справке.",
        TextWrapping = TextWrapping.Wrap, IsVisible = false
    };
    private readonly StackPanel _processCalibrationPanel = new() { Spacing = 6, IsVisible = false };
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

    public MainWindow()
    {
        _raster = new ExposureRasterService(_coordinates, _layout,
            new PanelizationService(_layout), new GerberRenderService(), new ExposureMaskService());
        Title = "PCB Expo | HALOT-MAGE S";
        Width = 1560;
        Height = 940;
        MinWidth = 1100;
        MinHeight = 700;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("365,*,275") };
        Content = root;
        var editor = new StackPanel { Spacing = 7, Margin = new Thickness(12) };
        var editorScroll = new ScrollViewer { Content = editor };
        Grid.SetColumn(editorScroll, 0);
        root.Children.Add(editorScroll);
        var center = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetColumn(center, 1);
        root.Children.Add(center);
        center.Children.Add(_preview);
        var hint = new TextBlock
        {
            Text = "Белое на preview означает, что LCD пропускает UV. Колесо: масштаб; правая кнопка: сдвиг; левая кнопка: перемещение платы в режиме «Одна плата».",
            Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(hint, 1);
        center.Children.Add(hint);
        var right = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        var rightScroll = new ScrollViewer { Content = right };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Section(editor, "Проект и импорт");
        editor.Children.Add(Button("Открыть папку Gerber", async () => await OpenFolder()));
        editor.Children.Add(Button("Открыть ZIP с Gerber", async () => await OpenZip()));
        editor.Children.Add(Button("Загрузить шаблон", async () => await OpenTemplate()));
        editor.Children.Add(Button("Открыть проект", async () => await OpenProject()));
        editor.Children.Add(Button("Сохранить проект", async () => await SaveProject()));
        editor.Children.Add(_files);
        Section(editor, "Режим экспозиции и слой Gerber");
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
        editor.Children.Add(_calibrationHint);
        editor.Children.Add(Button("Справка по калибровке", async () => await new CalibrationHelpWindow().ShowDialog(this)));
        _processCalibrationPanel.Children.Add(new TextBlock
        {
            Text = "Матрица зигзагов: строки — толщина/зазор, столбцы — компенсация. Каждая проба времени на свежем образце. Для оценки тонких линий увеличьте preview.",
            TextWrapping = TextWrapping.Wrap
        });
        _processCalibrationPanel.Children.Add(Button("Параметры теста и результат", async () => await ConfigureProcessCalibration()));
        editor.Children.Add(_processCalibrationPanel);
        editor.Children.Add(_layer);
        editor.Children.Add(new TextBlock { Text = "Контур платы (при необходимости выберите вручную)" });
        editor.Children.Add(_outline);
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

        Section(editor, "Профиль заготовки и механические отверстия");
        editor.Children.Add(new TextBlock
        {
            Text = "Отступы отверстий и точек центровки 10 мм приведены как пример. Перед экспозицией задайте размеры своей оснастки.",
            TextWrapping = TextWrapping.Wrap
        });
        editor.Children.Add(_profile);
        _profile.SelectionChanged += (_, _) =>
        {
            if (_syncing || _profile.SelectedItem is not string name) return;
            var profile = _profiles.Load().FirstOrDefault(x => x.Name == name);
            if (profile is null) return;
            _project.Blank = profile;
            RefreshBlankFields();
            RebuildPreview();
        };
        editor.Children.Add(Button("Сохранить профиль заготовки", SaveBlankProfile));
        AddBlankFields(editor);

        Section(editor, "Положение платы, мм от левого нижнего угла заготовки");
        _x = Number(editor, "Плата X", () => _project.PcbPositionMm.X,
            v => _project.PcbPositionMm = _project.PcbPositionMm with { X = v });
        _y = Number(editor, "Плата Y", () => _project.PcbPositionMm.Y,
            v => _project.PcbPositionMm = _project.PcbPositionMm with { Y = v });
        var align = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var command in Enum.GetValues<AlignmentCommand>())
            align.Children.Add(Button(UiText.Alignment(command), () => Align(command)));
        editor.Children.Add(align);
        Number(editor, "Отступ выравнивания, мм", () => _edgeInset, v => _edgeInset = v);

        Section(editor, "Размещение плат");
        _placement.ItemsSource = Enum.GetValues<CorePlacementMode>()
            .Select(x => new DisplayChoice<CorePlacementMode>(x, UiText.Placement(x))).ToArray();
        _placement.SelectionChanged += (_, _) =>
        {
            if (_syncing || _placement.SelectedItem is not DisplayChoice<CorePlacementMode> choice) return;
            _project.Panelization.Mode = choice.Value;
            RebuildPreview();
        };
        editor.Children.Add(_placement);
        Number(editor, "Интервал X, мм", () => _project.Panelization.SpacingXmm, v => _project.Panelization.SpacingXmm = v);
        Number(editor, "Интервал Y, мм", () => _project.Panelization.SpacingYmm, v => _project.Panelization.SpacingYmm = v);
        Number(editor, "Поле слева, мм", () => _project.Panelization.MarginLeftMm, v => _project.Panelization.MarginLeftMm = v);
        Number(editor, "Поле справа, мм", () => _project.Panelization.MarginRightMm, v => _project.Panelization.MarginRightMm = v);
        Number(editor, "Поле сверху, мм", () => _project.Panelization.MarginTopMm, v => _project.Panelization.MarginTopMm = v);
        Number(editor, "Поле снизу, мм", () => _project.Panelization.MarginBottomMm, v => _project.Panelization.MarginBottomMm = v);

        Section(editor, "Преобразования и экспозиция");
        editor.Children.Add(new TextBlock { Text = "Физический переворот Bottom относительно вертикальной оси центра всей заготовки.", TextWrapping = TextWrapping.Wrap });
        foreach (var box in new[] { _invert, _mirrorX, _mirrorY })
        {
            box.IsCheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                _project.CurrentTransform.Invert = _invert.IsChecked == true;
                _project.CurrentTransform.MirrorX = _mirrorX.IsChecked == true;
                _project.CurrentTransform.MirrorY = _mirrorY.IsChecked == true;
                RebuildPreview();
            };
            editor.Children.Add(box);
        }
        _time = Number(editor, "Время экспозиции, с", () => _project.CurrentExposureSeconds, SetExposureTime);
        _compensation = Number(editor, "Компенсация экспозиции, мм", () => _project.CurrentCompensationMm, SetCompensation);
        _pwm = new TextBox { PlaceholderText = "пусто = значение из шаблона" };
        editor.Children.Add(new TextBlock { Text = "PWM подсветки (необязательно, 1..255)" });
        editor.Children.Add(_pwm);
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
        editor.Children.Add(_aa);
        editor.Children.Add(Button("Показать preview в разрешении LCD", () => RebuildPreview(true)));

        Section(right, "Параметры принтера и проекта");
        right.Children.Add(_summary);
        Section(right, "Экспорт");
        right.Children.Add(Button("Точки центровки", () => SelectMode(ExposureMode.Registration)));
        right.Children.Add(Button("Калибровка", () => SelectMode(ExposureMode.Calibration)));
        right.Children.Add(Button("Время и компенсация", () => SelectMode(ExposureMode.ExposureCalibration)));
        right.Children.Add(Button("Экспортировать CXDLPV4", async () => await Export()));
        right.Children.Add(Button("Экспорт серии времени", async () => await ExportCalibrationSeries()));
        right.Children.Add(Button("Экспорт контуров в DXF", async () => await ExportDxf()));
        Section(right, "Состояние и предупреждения");
        right.Children.Add(_status);

        _preview.PositionChanged = (position, commit) =>
        {
            _x.Text = UiText.Number(position.X);
            _y.Text = UiText.Number(position.Y);
            if (commit) RebuildPreview();
        };
        Closed += (_, _) => { _preview.ClearPreview(); _package?.Dispose(); };
        _project.Exposure = _exposureProfiles.Load();
        _project.TemplatePath = Path.Combine(Environment.CurrentDirectory, "150x100.cxdlpv4");
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
        panel.Children.Add(new TextBlock { Text = "Название профиля" });
        _profileName = new TextBox { Text = _project.Blank.Name };
        _profileName.LostFocus += (_, _) => _project.Blank.Name = _profileName.Text ?? "";
        panel.Children.Add(_profileName);
        _blankFields.Add(Number(panel, "Ширина заготовки, мм", () => _project.Blank.WidthMm, v => _project.Blank.WidthMm = v));
        _blankFields.Add(Number(panel, "Высота заготовки, мм", () => _project.Blank.HeightMm, v => _project.Blank.HeightMm = v));
        _blankFields.Add(Number(panel, "Диаметр механических отверстий, мм", () => _project.Blank.RegistrationHoleDiameterMm, v => _project.Blank.RegistrationHoleDiameterMm = v));
        _blankFields.Add(Number(panel, "Отступ отверстий X, мм", () => _project.Blank.HoleInsetXmm, v => _project.Blank.HoleInsetXmm = v));
        _blankFields.Add(Number(panel, "Отступ отверстий Y, мм", () => _project.Blank.HoleInsetYmm, v => _project.Blank.HoleInsetYmm = v));
        _blankFields.Add(Number(panel, "Запретная зона вокруг отверстий, мм", () => _project.Blank.HoleClearanceMm, v => _project.Blank.HoleClearanceMm = v));
        _blankFields.Add(Number(panel, "Диаметр точек центровки, мм", () => _project.Blank.AlignmentPointDiameterMm, v => _project.Blank.AlignmentPointDiameterMm = v));
        _blankFields.Add(Number(panel, "Отступ точек центровки X, мм", () => _project.Blank.AlignmentInsetXmm, v => _project.Blank.AlignmentInsetXmm = v));
        _blankFields.Add(Number(panel, "Отступ точек центровки Y, мм", () => _project.Blank.AlignmentInsetYmm, v => _project.Blank.AlignmentInsetYmm = v));
    }

    private static void Section(StackPanel panel, string title) => panel.Children.Add(new TextBlock
    {
        Text = title, FontWeight = FontWeight.Bold, FontSize = 16, Margin = new Thickness(0, 10, 0, 2)
    });

    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 2) };
        button.Click += (_, _) => action();
        return button;
    }

    private TextBox Number(StackPanel panel, string label, Func<double> read, Action<double> write)
    {
        panel.Children.Add(new TextBlock { Text = label });
        var box = new TextBox { Text = UiText.Number(read()) };
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
        panel.Children.Add(box);
        return box;
    }

    private void RefreshBlankFields()
    {
        _profileName.Text = _project.Blank.Name;
        var values = new[] { _project.Blank.WidthMm, _project.Blank.HeightMm,
            _project.Blank.RegistrationHoleDiameterMm, _project.Blank.HoleInsetXmm,
            _project.Blank.HoleInsetYmm, _project.Blank.HoleClearanceMm,
            _project.Blank.AlignmentPointDiameterMm, _project.Blank.AlignmentInsetXmm,
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
        _exposureProfiles.Save(_project.Exposure);
    }

    private void SelectMode(ExposureMode mode) => _mode.SelectedItem =
        _mode.ItemsSource?.Cast<DisplayChoice<ExposureMode>>().FirstOrDefault(x => x.Value == mode);

    private void SyncModeFields()
    {
        _syncing = true;
        _mode.SelectedItem = _mode.ItemsSource?.Cast<DisplayChoice<ExposureMode>>()
            .FirstOrDefault(x => x.Value == _project.Mode);
        _calibrationHint.IsVisible = _project.Mode == ExposureMode.Calibration;
        var processCalibration = _project.Mode == ExposureMode.ExposureCalibration;
        _processCalibrationPanel.IsVisible = processCalibration;
        _compensation.IsEnabled = !processCalibration;
        _mirrorX.IsEnabled = _mirrorY.IsEnabled = _aa.IsEnabled = !processCalibration;
        _placement.SelectedItem = _placement.ItemsSource?.Cast<DisplayChoice<CorePlacementMode>>()
            .FirstOrDefault(x => x.Value == _project.Panelization.Mode);
        _invert.IsChecked = _project.CurrentTransform.Invert;
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
            _status.Text = $"Шаблон загружен: {Path.GetFileName(path)}";
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
                         GerberLayerKind.TopSolderMask, GerberLayerKind.BottomSolderMask })
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
            var importStatus = package.OutlineFallback ? "ВНИМАНИЕ: контур платы отсутствует, габариты оценены по artwork." :
                ambiguous.Length > 0 ? $"Требуется ручной выбор: {string.Join(", ", ambiguous)}." :
                $"Импортировано {package.Layers.Count} файлов.";
            SyncModeFields();
            RebuildPreview();
            _status.Text = importStatus;
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }

    private void RebuildPreview(bool fullResolution = false)
    {
        if (_printer is null) { UpdateSummary(); return; }
        if (_package is null && _project.Mode is not (ExposureMode.Registration or ExposureMode.Calibration or ExposureMode.ExposureCalibration))
        {
            _preview.ClearPreview();
            _boardCount = 0;
            _positions = [];
            _status.Text = "Импортируйте Gerber, чтобы показать экспозицию платы.";
            UpdateSummary();
            return;
        }
        try
        {
            var native = fullResolution || _project.Mode == ExposureMode.ExposureCalibration;
            var raster = native ? RasterGeometry.Native(_printer) : RasterGeometry.Preview(_printer);
            using var result = _raster.Build(_project, _printer, raster, _package?.BoardBoundsMm);
            var image = CvInvoke.Imencode(".png", result.Image);
            using var stream = new MemoryStream(image);
            var bitmap = new Bitmap(stream);
            _preview.SetPreview(bitmap, _printer, _project, result.BlankOnLcd, result.Boards.ToArray());
            _boardCount = result.Boards.Count;
            _positions = result.Boards.ToArray();
            _status.Text = native ? "Маска в разрешении LCD сформирована и показана в preview." :
                "Preview обновлён. Белое означает, что LCD пропускает UV.";
        }
        catch (Exception error)
        {
            _preview.ClearPreview();
            _boardCount = 0;
            _positions = [];
            ShowError(error);
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_printer is null) { _summary.Text = "Загрузите шаблон CXDLPV4."; return; }
        _summary.Text = UiText.Summary(_printer, _project, _package, _boardCount, _positions);
    }

    private void ShowError(Exception error)
    {
        _status.Text = error.Message;
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
            _status.Text = $"Профиль {_project.Blank.Name} сохранён локально.";
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
            Title = "Сохранить проект PCB Expo", SuggestedFileName = $"{SafeName(_project.Name)}.pcbexpo.json",
            FileTypeChoices = [new FilePickerFileType("PCB Expo") { Patterns = ["*.pcbexpo.json"] }]
        });
        if (path is null) return;
        try
        {
            _projects.Save(_project, path.Path.LocalPath);
            _status.Text = $"Проект сохранён: {path.Path.LocalPath}";
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
        var filename = $"{SafeName(_project.Name)}_{_project.Mode}_{blankName}.cxdlpv4";
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
            _log.Write($"Export={output.Path}; mode={_project.Mode}; blank={_project.Blank.WidthMm}x{_project.Blank.HeightMm}; boards={result.Boards.Count}; positions={string.Join(';', result.Boards)}; mirror={_project.CurrentTransform.MirrorX},{_project.CurrentTransform.MirrorY}; invert={_project.CurrentTransform.Invert}; exposure={_project.CurrentExposureSeconds}; compensation={_project.CurrentCompensationMm}; diffPixels={output.DifferentPixels}");
            _status.Text = $"Экспорт и повторное чтение успешны: {output.Path}";
        }
        catch (Exception error) { ShowError(error); }
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
            if (target is not null) _status.Text = $"Результат калибровки применён к {(target == CalibrationApplyTarget.Copper ? "меди" : "паяльной маске")}: {UiText.Number(time)} с, {UiText.Number(settings.SelectedCompensationMm)} мм.";
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
            var progress = new Progress<string>(message => _status.Text = message);
            var result = await Task.Run(() => new ExposureCalibrationExportService().Export(_project, _printer,
                folders[0].Path.LocalPath, progress));
            _status.Text = $"Серия калибровки сохранена: {result.Directory}. Файлов: {result.Files.Count}; повторное чтение каждого успешно. Каждый опыт — на свежем образце.";
            _log.Write($"ExposureCalibrationExport={result.Directory}; files={result.Files.Count}");
        }
        catch (Exception error) { ShowError(error); }
        finally { _exportingCalibration = false; ((Control)Content!).IsEnabled = wasEnabled; }
    }

    private async Task ExportDxf()
    {
        var contours = new ContourService();
        var sources = new List<DxfSource>
        {
            new("Заготовка и механические отверстия", "blank",
                "Прямоугольник заготовки и четыре окружности отверстий с точными размерами профиля. Начало координат — левый нижний угол заготовки.",
                () => contours.Blank(_project.Blank)),
            new("Точки центровки", "registration",
                "Пять окружностей с диаметром и координатами светящихся точек из профиля. Начало координат — левый нижний угол заготовки.",
                () => contours.Registration(_project.Blank)),
            new("Калибровочный рисунок", "calibration",
                "Открытая линия 100 мм и замкнутый квадрат 50 × 50 мм с точными эталонными размерами. Начало координат — левый нижний угол заготовки.",
                () => contours.Calibration(_project.Blank))
        };
        if (_package is not null && _project.LayerPaths.TryGetValue(GerberLayerKind.BoardOutline, out var outlinePath))
            sources.Add(new("Контур платы (линии Gerber)", "board_outline",
                "Осевые линии и дуги выбранного контура платы, без толщины апертуры. Координаты исходного Gerber; размещение и переворот не применяются.",
                () => new GerberOutlineService().Read(outlinePath)));
        if (_printer is { } printer)
            sources.Add(new($"Текущая экспозиция: {UiText.Exposure(_project.Mode)}", $"exposure_{_project.Mode}",
                $"Границы белых областей финальной маски с текущими преобразованиями, компенсацией и размещением плат. Начало координат — левый нижний угол заготовки. Точность ограничена шагом LCD: X {printer.PixelPitchXmm:F6}, Y {printer.PixelPitchYmm:F6} мм.",
                () =>
                {
                    var raster = RasterGeometry.Native(printer);
                    using var mask = _raster.Build(_project, printer, raster, _package?.BoardBoundsMm);
                    return contours.FromMask(mask.Image, raster, new PointMm(-mask.BlankOnLcd.X, -mask.BlankOnLcd.Y));
                }));
        if (_package is not null)
            foreach (var layer in _package.Layers.Where(l => l.Kind != GerberLayerKind.Drill))
                sources.Add(new($"Gerber: {layer.RelativePath}", SafeName(Path.GetFileNameWithoutExtension(layer.Name)),
                    "Внешние и внутренние границы рисунка слоя, включая толщину линий и апертуры. Координаты исходного Gerber; размещение и преобразования экспозиции не применяются. Контуры получены из растра с шагом 0,01 мм.",
                    () => contours.Gerber(layer.Path)));
        var protectedPaths = (_package?.Layers.Select(l => l.Path) ?? [])
            .Concat([_project.TemplatePath, _project.GerberSourcePath]);
        var dialog = new DxfExportWindow(_project.Name, sources, protectedPaths)
        {
            Exported = (path, count) =>
            {
                _status.Text = $"DXF сохранён: {path}. Контуров: {count}; единицы — мм.";
                _log.Write($"DxfExport={path}; contours={count}; units=mm");
            }
        };
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ConfirmExport()
    {
        var dialog = new Window { Title = "Параметры экспорта", Width = 480, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        stack.Children.Add(new ScrollViewer { Content = new TextBlock { Text = _summary.Text, TextWrapping = TextWrapping.Wrap }, Height = 350 });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(Button("Отмена", () => dialog.Close(false)));
        row.Children.Add(Button("Экспортировать", () => dialog.Close(true)));
        stack.Children.Add(row);
        dialog.Content = stack;
        return await dialog.ShowDialog<bool>(this);
    }

    private static string SafeName(string value) => new(value.Select(c =>
        Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}
