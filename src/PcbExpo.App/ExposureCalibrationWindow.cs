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
        Heading("Матрица параллельных зигзагов");
        Text("Строки R — толщина/зазор, столбцы C — компенсация. В ячейке шесть зигзагов по горизонтали и шесть по вертикали. Значения разделяйте ;, десятичные запятая и точка поддерживаются.");
        var settings = project.ProcessCalibration;
        _widths = Field("Толщины линий, мм", Join(settings.LineWidthsMm));
        _gaps = Field("Зазоры, мм", Join(settings.GapsMm));
        _compensations = Field("Компенсации столбцов, мм", Join(settings.CompensationsMm));
        Text("Плюс расширяет белую область, минус сужает. При инверсии линии тёмные на белом поле. Компенсация — сдвиг края, а не изменение полной ширины.");
        _times = Field("Времена для серии файлов, с", Join(settings.TimesSeconds));
        _times.PlaceholderText = "Задайте времена для своего материала через ;";
        Text("«Экспорт серии времени» в главном окне создаёт по одному файлу на время. Каждый опыт — на свежем образце.");
        var around = new Button { Content = "Пять времён вокруг выбранного: 60–140 %" };
        around.Click += (_, _) => GenerateTimes();
        panel.Children.Add(around);
        Heading("Выбор по проявленному образцу");
        Text("Проверьте нужную толщину/зазор у обеих групп H/V: линии непрерывны, просветы открыты, перемычек нет. Начните со столбца 0. Исчезнувшие линии — не успешная проба.");
        _selectedTime = Field("Время текущего / выбранного опыта, с", UiText.Number(project.Exposure.ProcessCalibrationSeconds));
        _selectedCompensation = Field("Компенсация выбранного столбца, мм", UiText.Number(settings.SelectedCompensationMm));
        Text("Применение записывает время и компенсацию в общий профиль Top/Bottom выбранного материала. Полярность и PWM проверьте отдельно.");
        var instructions = new Expander { Header = "Подробная процедура и таблица результатов", Content = new TextBlock
            { Text = ExposureCalibrationExportService.Instructions, TextWrapping = TextWrapping.Wrap } };
        panel.Children.Add(instructions);
        root.Children.Add(new ScrollViewer { Content = panel });
        _status.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_status, 1); root.Children.Add(_status);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        AddButton("Закрыть", () => Close());
        AddButton("Сохранить параметры", () => Commit(null));
        AddButton("Применить к меди", () => Commit(CalibrationApplyTarget.Copper));
        AddButton("Применить к маске", () => Commit(CalibrationApplyTarget.SolderMask));
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
                throw new InvalidOperationException("Для применения результата укажите время от 0 до 3600 с (ноль не допускается).");
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
        { _status.Text = "Сначала задайте положительное время опыта ниже."; return; }
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
