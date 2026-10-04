using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcbExpo.Core;

namespace PcbExpo.App;

internal sealed class CalibrationHelpWindow : Window
{
    public CalibrationHelpWindow()
    {
        Title = "Справка: масштаб, время и компенсация";
        Width = 650; Height = 650; MinWidth = 450; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock { Text = "Проверка физического масштаба LCD", FontSize = 21,
            FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Add("Калибровка помогает проверить размеры изображения по обеим осям и его ориентацию перед экспонированием платы. Gerber для этого режима не требуется.");
        content.Children.Add(new CalibrationDiagram { Height = 165 });
        Add($"Эталон: горизонтальная линия {UiText.Number(CalibrationPattern.LineLengthMm)} мм; квадрат {UiText.Number(CalibrationPattern.SquareSizeMm)} × {UiText.Number(CalibrationPattern.SquareSizeMm)} мм. Рисунок располагается по центру заготовки. Толщина линий — один пиксель LCD.");
        Add("1. Загрузите шаблон CXDLPV4 своего принтера. Задайте заготовку не меньше 110 × 70 мм, которая помещается на LCD, и выберите режим «Калибровка».");
        Add("2. Укажите время экспозиции для контрольного материала; при необходимости задайте PWM. Время хранится отдельно от режимов меди, маски и точек центровки.");
        Add("3. Проверьте рисунок через «Показать preview в разрешении LCD», затем нажмите «Экспортировать CXDLPV4». Получится один экспозиционный слой. Для бумажного или CAD-эталона можно отдельно экспортировать «Калибровочный рисунок» в DXF в масштабе 1:1.");
        Add("4. Выведите рисунок на принтере или сделайте контрольную экспозицию. Измерьте расстояние между концами линии, а у квадрата — горизонтальную и вертикальную стороны между серединами светящихся линий. Измерения на уменьшенном preview не проверяют физический масштаб LCD.");
        Add("5. Ожидаемые размеры: линия по X — 100,00 мм, квадрат по X и Y — по 50,00 мм. Сравнивайте X и Y отдельно: их шаг пикселя может различаться. Если линия и ширина квадрата дают разные относительные ошибки, повторите измерение.");
        Add("При расхождении проверьте физический размер поля LCD и совместимость выбранного шаблона с принтером. Приложение берёт масштаб из шаблона; автоматической коррекции по измерениям пока нет. Масштаб DXF задаётся в миллиметрах и не подтверждает масштаб реального LCD.");
        Add("Инверсия, пользовательское зеркалирование, компенсация, размещение плат и переворот Bottom не изменяют калибровочный рисунок. Механические отверстия, видимые в preview, служат подсказкой и в экспозицию не входят.");
        content.Children.Add(new TextBlock { Text = "Подбор времени и компенсации", FontSize = 21,
            FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Add("Этот опыт подбирает время для материала и поправку размеров линий и просветов. После проверки масштаба выберите «Калибровка времени и компенсации». Gerber не нужен; стандартная матрица помещается на заготовке 150 × 100 мм. Приложение проверяет размер матрицы и не масштабирует тестовые элементы.");
        Add("1. Нажмите «Настроить калибровку». Задайте толщины линий, зазоры и компенсации, разделяя значения точкой с запятой; десятичная запятая и точка допустимы. По умолчанию толщины 0,10 / 0,15 / 0,20 мм, зазоры 0,05 / 0,10 / 0,15 / 0,20 мм, компенсации −0,05 / −0,025 / 0 / +0,025 / +0,05 мм. Строки R — пары толщины и зазора, столбцы C — компенсации. В каждой ячейке есть горизонтальная H и вертикальная V группы зигзагов.");
        Add("2. Выберите инверсию для своего процесса. Без инверсии линии белые; при инверсии — тёмные на белом поле ячейки. Белое пропускает UV. Положительная компенсация расширяет белые области, отрицательная сужает: ширина прямой белой линии меняется приблизительно на удвоенную компенсацию. Для тёмных линий эффект противоположный. Например, +0,025 мм расширяет белую линию примерно на 0,05 мм и сужает соседний тёмный просвет.");
        Add("Компенсация округляется до целого числа пикселей отдельно по X и Y. Меньше половины шага пикселя — нулевой сдвиг по этой оси. Зеркалирование и сглаживание в этом тесте отключены; общее поле компенсации заменено списком столбцов. Увеличьте preview для просмотра тонких элементов.");
        Add("3. Задайте предполагаемое время в поле экспозиции и список времён в настройках. Можно сформировать пять проб от 60 до 140 % предполагаемого времени. Эти значения — диапазон опыта, а не готовый рецепт. «Экспортировать CXDLPV4» сохраняет одну пробу текущего времени; «Экспортировать пробы с разным временем» создаёт отдельную папку с файлами, параметрами, инструкцией и таблицей results.tsv.");
        Add("4. Каждый файл экспонируйте один раз на свежем образце. Последовательные засветки одного образца суммируют дозу и не позволяют сравнить времена. Во всех пробах сохраняйте одинаковыми материал, толщину, подготовку, контакт, PWM и проявление. Время напечатано на тесте.");
        Add("5. После проявления сравните строки с нужными для платы толщинами и зазорами. Сначала сравнивайте времена в столбце с компенсацией 0. Успех: непрерывные линии и открытые просветы без перемычек у обеих групп H/V, в том числе на поворотах. Открытый зазор с исчезнувшей линией не считается успехом. Запишите наблюдения H/V в results.tsv.");
        Add("6. Для подходящего времени сравните столбцы компенсации. При нескольких успешных вариантах выбирайте поправку ближе к нулю и повторите опыт для подтверждения. Для меди проверьте результат также после травления; автоматической оценки фотографии нет.");
        Add("7. В «Настроить калибровку» введите проверенные время и компенсацию, затем нажмите «Применить к меди» или «Применить к паяльной маске». Обновится общий профиль Top/Bottom выбранного процесса; инверсия и PWM сохранят свои значения. Для паяльного трафарета задайте проверенные значения отдельно в его режиме: толщина фотополимера и процесс изготовления могут требовать другого времени.");
        content.Children.Add(new TextBlock { Text = "Интерпретация результатов", FontSize = 21,
            FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = ExposureCalibrationExportService.ResultInterpretation,
            TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 0, 14, 0) });
        root.Children.Add(new ScrollViewer { Content = content });
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 1); root.Children.Add(close);
        Content = root;

        void Add(string text) => content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    }
}

internal sealed class CalibrationDiagram : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min((Bounds.Width - 30) / 110, (Bounds.Height - 30) / 70);
        if (scale <= 0) return;
        var pattern = CalibrationPattern.Create(new BlankProfile { WidthMm = 110, HeightMm = 70 });
        var left = (Bounds.Width - 110 * scale) / 2;
        var top = (Bounds.Height - 70 * scale) / 2;
        Point Position(PointMm p) => new(left + p.X * scale, top + (70 - p.Y) * scale);
        context.FillRectangle(Brushes.Black, new Rect(left, top, 110 * scale, 70 * scale));
        var pen = new Pen(Brushes.White, 1);
        context.DrawLine(pen, Position(pattern.LineStart), Position(pattern.LineEnd));
        var a = Position(new PointMm(pattern.Square.X, pattern.Square.Top));
        context.DrawRectangle(null, pen, new Rect(a.X, a.Y, pattern.Square.Width * scale, pattern.Square.Height * scale));
    }
}
