using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcbExpo.Core;

namespace PcbExpo.App;

internal enum CalibrationApplyTarget { Copper, SolderMask }

internal sealed class ExposureCalibrationWindow : Window
{
    private readonly ProjectModel _project;
    private readonly PrinterInfo? _printer;
    private readonly Action<ExposureCalibrationSettings, double, CalibrationApplyTarget?> _save;
    private readonly TextBox _widths;
    private readonly TextBox _gaps;
    private readonly TextBox _compensations;
    private readonly TextBox _times;
    private readonly TextBox _selectedTime;
    private readonly TextBox _selectedCompensation;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public ExposureCalibrationWindow(ProjectModel project, PrinterInfo? printer,
        Action<ExposureCalibrationSettings, double, CalibrationApplyTarget?> save)
    {
        _project = project; _printer = printer; _save = save;
        Title = "Калибровка времени и компенсации";
        Width = 780; Height = 830; MinWidth = 540; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(20) };
        var panel = new StackPanel { Spacing = 7 };
        Heading("Размеры линий и просветов тестового рисунка");
        Text("Каждая строка R проверяет одну пару «толщина линии / зазор». Создаются все сочетания двух списков ниже. Каждый столбец C повторяет эти пары с другой компенсацией. В ячейке две группы по 6 зигзагов: H — горизонтальная, V — вертикальная.");
        Text("Введите несколько значений через точку с запятой, например 0,10; 0,15; 0,20. Десятичную часть можно отделять запятой или точкой. Все размеры указаны в миллиметрах.");
        var settings = project.ProcessCalibration;
        _widths = Field("Номинальные толщины линий до компенсации, мм", Join(settings.LineWidthsMm));
        Text("Ширина каждого штриха зигзага. От 0,02 до 1 мм, максимум 6 разных значений.");
        _gaps = Field("Номинальные просветы между соседними линиями, мм", Join(settings.GapsMm));
        Text("Минимальное расстояние между краями соседних наклонных штрихов, по нормали к штриху. От 0,02 до 1 мм, максимум 6 разных значений.");
        _compensations = Field("Смещение края для каждого столбца, мм", Join(settings.CompensationsMm));
        Text("0 — исходный рисунок. Плюс расширяет белую область, минус сужает. Например, +0,025 мм увеличивает ширину прямой белой линии примерно на 0,05 мм и уменьшает просвет примерно на 0,05 мм. При инверсии белым становится фон, поэтому тёмные линии меняются противоположным образом. От −0,5 до +0,5 мм, максимум 7 столбцов. Фактический сдвиг округляется до пикселей LCD отдельно по X и Y.");
        Heading("Время одиночной пробы и серия сравнений");
        _selectedTime = Field("Время одной пробы / выбранного результата, с", UiText.Number(project.Exposure.ProcessCalibrationSeconds));
        Text("Это время указано на preview и используется кнопкой «Экспортировать CXDLPV4» для одного теста. После проверки образцов сюда вводится время удачной пробы. Для экспорта и применения результата время должно быть больше нуля.");
        _times = Field("Список времён для отдельных проб, с", Join(settings.TimesSeconds));
        _times.PlaceholderText = "Задайте времена для своего материала через ;";
        Text("Кнопка «Экспортировать пробы с разным временем» в главном окне создаёт отдельный CXDLPV4 для каждого времени из этого списка, максимум 12 файлов. Для каждого файла нужен свежий образец: повторные экспозиции одной платы складывают дозу.");
        var around = new Button { Content = "Заполнить список: 60, 80, 100, 120 и 140 % от времени пробы", HorizontalAlignment = HorizontalAlignment.Stretch };
        around.Content = new TextBlock { Text = "Заполнить список: 60, 80, 100, 120 и 140 % от времени пробы", TextWrapping = TextWrapping.Wrap };
        around.Click += (_, _) => GenerateTimes();
        panel.Children.Add(around);
        Heading("Результат после проявления образцов");
        Text("Найдите строку с нужными толщиной и просветом. Начните со столбца, где компенсация равна 0, затем сравните остальные. Проба подходит, если у обеих групп H и V все линии непрерывны, а просветы открыты без перемычек. Исчезнувшие линии не считаются удачным результатом.");
        _selectedCompensation = Field("Компенсация удачного столбца, мм", UiText.Number(settings.SelectedCompensationMm));
        Text("Введите значение с подписи столбца, а не его номер C. В поле времени выше укажите время этого образца. «Сохранить параметры теста» обновляет тест без изменения производственных профилей. «Применить к меди» или «Применить к паяльной маске» записывает выбранные время и компенсацию сразу для Top и Bottom соответствующего материала. Инверсия и PWM не изменяются.");
        var instructions = new Expander { Header = "Подробная процедура и таблица результатов", Content = new TextBlock
            { Text = ExposureCalibrationExportService.Instructions, TextWrapping = TextWrapping.Wrap } };
        panel.Children.Add(instructions);
        root.Children.Add(new ScrollViewer { Content = panel });
        _status.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_status, 1); root.Children.Add(_status);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        AddButton("Закрыть", () => Close());
        AddButton("Сохранить параметры теста", () => Commit(null));
        AddButton("Применить к меди", () => Commit(CalibrationApplyTarget.Copper));
        AddButton("Применить к паяльной маске", () => Commit(CalibrationApplyTarget.SolderMask));
        Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        Content = root;
        try { _status.Text = Describe(ExposureCalibrationPattern.Create(project.Blank, settings), settings); }
        catch (Exception error) { _status.Text = error.Message; }

        void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 18,
            FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        void Text(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        TextBox Field(string title, string value)
        {
            Text(title);
            var box = new TextBox { Text = value };
            panel.Children.Add(box); return box;
        }
        void AddButton(string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(3) };
            button.Click += (_, _) => action(); buttons.Children.Add(button);
        }
    }

    private void Commit(CalibrationApplyTarget? target)
    {
        try
        {
            var settings = new ExposureCalibrationSettings
            {
                LineWidthsMm = Parse(_widths.Text), GapsMm = Parse(_gaps.Text),
                CompensationsMm = Parse(_compensations.Text), TimesSeconds = Parse(_times.Text)
            };
            if (!UiText.TryNumber(_selectedTime.Text, out var time) || time < 0 || time > 3600 || target is not null && time <= 0)
                throw new InvalidOperationException(target is null
                    ? "Время теста должно быть от 0 до 3600 с; перед экспортом задайте положительное время."
                    : "Для применения результата укажите время больше 0 и не больше 3600 с.");
            if (!UiText.TryNumber(_selectedCompensation.Text, out var compensation) || !settings.CompensationsMm.Contains(compensation))
                throw new InvalidOperationException("Компенсация результата должна совпадать с одним из значений столбцов.");
            settings.SelectedCompensationMm = compensation;
            var pattern = ExposureCalibrationPattern.Create(_project.Blank, settings);
            _save(settings, time, target);
            _status.Text = Describe(pattern, settings) + (target is null ? " Параметры сохранены." : " Проверенный результат применён.");
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    private void GenerateTimes()
    {
        if (!UiText.TryNumber(_selectedTime.Text, out var time) || time <= 0)
        { _status.Text = "Сначала задайте положительное значение в поле «Время одной пробы / выбранного результата»."; return; }
        _times.Text = Join(new[] { 0.6, 0.8, 1.0, 1.2, 1.4 }.Select(k => Math.Round(time * k, 3)));
    }

    private string Describe(ExposureCalibrationPattern pattern, ExposureCalibrationSettings settings)
    {
        var text = $"Матрица {UiText.Number(Math.Round(pattern.Bounds.Width, 2))} × {UiText.Number(Math.Round(pattern.Bounds.Height, 2))} мм; " +
            $"ячеек {pattern.Cells.Count}, зигзагов {pattern.Cells.Count * 12}.";
        if (_printer is { } printer)
        {
            text += $" Шаг LCD: X {printer.PixelPitchXmm:F6}, Y {printer.PixelPitchYmm:F6} мм.";
            var values = settings.CompensationsMm.Select(c => (Math.Sign(c),
                CoordinateTransformService.MmToPx(Math.Abs(c), 1 / printer.PixelPitchXmm),
                CoordinateTransformService.MmToPx(Math.Abs(c), 1 / printer.PixelPitchYmm))).ToArray();
            // A compensation rounded to zero is the same as the zero column regardless of its sign.
            var effective = values.Select(v => (v.Item2 == 0 && v.Item3 == 0 ? 0 : v.Item1, v.Item2, v.Item3));
            if (effective.Distinct().Count() < values.Length) text += " Некоторые компенсации дают одинаковый сдвиг на этом LCD: увеличьте шаг между столбцами.";
        }
        return text;
    }

    private static List<double> Parse(string? text) => (text ?? "").Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(s => UiText.TryNumber(s, out var value) ? value : throw new InvalidOperationException($"Не удалось прочитать число «{s}». Разделитель значений — ;")).ToList();
    private static string Join(IEnumerable<double> values) => string.Join("; ", values.Select(UiText.Number));
}
