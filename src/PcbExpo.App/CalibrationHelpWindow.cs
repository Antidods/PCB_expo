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
        Title = "Справка: режим калибровки";
        Width = 650; Height = 650; MinWidth = 450; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock { Text = "Проверка физического масштаба LCD", FontSize = 21,
            FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        Add("Калибровка помогает проверить размеры изображения по обеим осям и его ориентацию перед экспонированием платы. Gerber для этого режима не требуется.");
        Add("Для подбора технологического времени и проверки слипания тонких линий выберите режим «Калибровка времени и компенсации». Кнопка «Настроить калибровку» задаёт размеры линий и просветов, компенсации столбцов и времена проб. Кнопка «Экспортировать пробы с разным временем» создаёт отдельный файл для каждого времени. Каждый файл экспонируйте на свежем образце.");
        content.Children.Add(new CalibrationDiagram { Height = 165 });
        Add($"Эталон: горизонтальная линия {UiText.Number(CalibrationPattern.LineLengthMm)} мм; квадрат {UiText.Number(CalibrationPattern.SquareSizeMm)} × {UiText.Number(CalibrationPattern.SquareSizeMm)} мм. Рисунок располагается по центру заготовки. Толщина линий — один пиксель LCD.");
        Add("1. Загрузите шаблон CXDLPV4 своего принтера. Задайте заготовку не меньше 110 × 70 мм, которая помещается на LCD, и выберите режим «Калибровка».");
        Add("2. Укажите время экспозиции для контрольного материала; при необходимости задайте PWM. Время хранится отдельно от режимов меди, маски и точек центровки.");
        Add("3. Проверьте рисунок через «Показать preview в разрешении LCD», затем нажмите «Экспортировать CXDLPV4». Получится один экспозиционный слой. Для бумажного или CAD-эталона можно отдельно экспортировать «Калибровочный рисунок» в DXF в масштабе 1:1.");
        Add("4. Выведите рисунок на принтере или сделайте контрольную экспозицию. Измерьте расстояние между концами линии, а у квадрата — горизонтальную и вертикальную стороны между серединами светящихся линий. Измерения на уменьшенном preview не проверяют физический масштаб LCD.");
        Add("5. Ожидаемые размеры: линия по X — 100,00 мм, квадрат по X и Y — по 50,00 мм. Сравнивайте X и Y отдельно: их шаг пикселя может различаться. Если линия и ширина квадрата дают разные относительные ошибки, повторите измерение.");
        Add("При расхождении проверьте физический размер поля LCD и совместимость выбранного шаблона с принтером. Приложение берёт масштаб из шаблона; автоматической коррекции по измерениям пока нет. Масштаб DXF задаётся в миллиметрах и не подтверждает масштаб реального LCD.");
        Add("Инверсия, пользовательское зеркалирование, компенсация, размещение плат и переворот Bottom не изменяют калибровочный рисунок. Механические отверстия, видимые в preview, служат подсказкой и в экспозицию не входят.");
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
