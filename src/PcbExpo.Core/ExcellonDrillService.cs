using System.Globalization;
using System.Text.RegularExpressions;

namespace PcbExpo.Core;

/// <summary>Читает отверстия и прямые пазы Excellon без растеризации геометрии.</summary>
public sealed partial class ExcellonDrillService
{
    public IReadOnlyList<DxfContour> Read(string path, bool allowEmpty = false)
    {
        var contours = new List<DxfContour>();
        var tools = new Dictionary<int, double>();
        double? units = null;
        int? tool = null;
        var integers = 0; var decimals = 0;
        var leadingIncluded = false;
        var zeroModeSet = false;
        var header = false; var body = false; var finished = false;
        var incremental = false; var routing = false; var cutting = false;
        var current = new PointMm(0, 0);
        var lineNumber = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            var line = raw.Trim().ToUpperInvariant();
            if (line.Length == 0) continue;
            var format = FileFormat().Match(line);
            if (format.Success)
            {
                SetFormat(int.Parse(format.Groups[1].Value), int.Parse(format.Groups[2].Value));
                continue;
            }
            line = line.Split(';')[0].Trim().Replace(" ", "");
            if (line.Length == 0) continue;
            if (!header)
            {
                if (line != "M48") throw Error("Файл должен начинаться с M48.");
                header = true; continue;
            }
            if (line == "M30") { finished = true; break; }
            if (line is "%" or "M95") { body = true; continue; }
            var unit = UnitDeclaration().Match(line);
            if (unit.Success)
            {
                if (body) throw Error("Смена единиц после заголовка не поддерживается.");
                units = unit.Groups[1].Value == "METRIC" ? 1 : 25.4;
                leadingIncluded = unit.Groups[2].Value == "LZ";
                zeroModeSet = unit.Groups[2].Success;
                if (unit.Groups[3].Success) SetFormat(unit.Groups[3].Length, unit.Groups[4].Length);
                continue;
            }
            if (line is "M71" or "M72")
            {
                if (body) throw Error("Смена единиц после заголовка не поддерживается.");
                units = line == "M71" ? 1 : 25.4; continue;
            }
            if (line == "FMAT,2") continue;
            if (line is "G90" or "ICI,OFF") { incremental = false; continue; }
            if (line is "G91" or "ICI,ON") { incremental = true; continue; }
            if (line == "M15") { cutting = true; continue; }
            if (line == "M16") { cutting = false; continue; }
            var toolCommand = ToolCommand().Match(line);
            if (toolCommand.Success)
            {
                var number = int.Parse(toolCommand.Groups[1].Value, CultureInfo.InvariantCulture);
                if (toolCommand.Groups[2].Success)
                {
                    if (units is null) throw Error("Единицы METRIC/INCH должны быть заданы до диаметров инструмента.");
                    var diameter = double.Parse(toolCommand.Groups[2].Value, CultureInfo.InvariantCulture) * units.Value;
                    if (!double.IsFinite(diameter) || diameter <= 0 || tools.ContainsKey(number))
                        throw Error("Некорректный или повторный диаметр инструмента.");
                    tools.Add(number, diameter);
                }
                if (body)
                {
                    if (number == 0) { tool = null; continue; }
                    if (!tools.ContainsKey(number)) throw Error($"Не задан диаметр инструмента T{number}.");
                    tool = number;
                }
                continue;
            }
            if (!body) throw Error("Неизвестная команда заголовка.");
            if (units is null) throw Error("Не заданы единицы METRIC/INCH.");
            if (line.StartsWith("G00", StringComparison.Ordinal)) { routing = true; cutting = false; line = line[3..]; }
            else if (line.StartsWith("G01", StringComparison.Ordinal))
            {
                if (!cutting) throw Error("Перед прямым фрезерованием G01 нужна команда M15.");
                routing = true; line = line[3..];
            }
            else if (line.StartsWith("G05", StringComparison.Ordinal) || line.StartsWith("G81", StringComparison.Ordinal))
            { routing = false; cutting = false; line = line[3..]; }
            if (line.Length == 0) continue;
            if (tool is null) throw Error("Перед координатами выберите инструмент T с заданным диаметром.");
            var diameterMm = tools[tool.Value];
            var slot = line.IndexOf("G85", StringComparison.Ordinal);
            if (slot >= 0)
            {
                var start = slot == 0 ? current : Position(line[..slot], current);
                var end = Position(line[(slot + 3)..], start);
                contours.Add(Slot(start, end, diameterMm));
                current = end;
            }
            else
            {
                var next = Position(line, current);
                if (!routing) contours.Add(new DxfCircle("DRILL", next, diameterMm / 2));
                else if (cutting) contours.Add(Slot(current, next, diameterMm));
                current = next;
            }
        }
        if (!header || !body || !finished) throw Error("Неполный Excellon: нужны M48, конец заголовка и M30.");
        if (contours.Count == 0 && !allowEmpty) throw Error("В файле нет отверстий или поддерживаемых прямых пазов.");
        return contours;

        InvalidDataException Error(string message) => new($"{Path.GetFileName(path)}, строка {lineNumber}: {message} Неподдерживаемые команды не пропускаются при экспорте для CNC.");
        void SetFormat(int whole, int fraction)
        {
            if (whole is < 1 or > 6 || fraction is < 1 or > 6 ||
                integers != 0 && (integers != whole || decimals != fraction))
                throw Error("Некорректный или противоречивый формат координат.");
            integers = whole; decimals = fraction;
        }
        PointMm Position(string command, PointMm previous)
        {
            var matches = Coordinates().Matches(command).Cast<Match>().ToArray();
            if (matches.Length == 0 || string.Concat(matches.Select(m => m.Value)) != command ||
                matches.Select(m => m.Groups[1].Value).Distinct().Count() != matches.Length)
                throw Error("Неподдерживаемая команда сверловки. Поддерживаются X/Y, G85 и прямые G00/G01 с M15/M16.");
            var x = previous.X; var y = previous.Y;
            foreach (var match in matches)
            {
                var value = match.Groups[2].Value;
                double coordinate;
                if (value.Contains('.')) coordinate = double.Parse(value, CultureInfo.InvariantCulture);
                else
                {
                    if (integers == 0 || !zeroModeSet)
                        throw Error("Для целочисленных координат нужны ;FILE_FORMAT=целые:дробные или маска 000.000, а также LZ/TZ. Используйте экспорт с десятичной точкой.");
                    var digits = value.TrimStart('+', '-');
                    if (digits.Length > integers + decimals) throw Error("Координата длиннее объявленного формата.");
                    // Excellon LZ означает сохранённые ведущие нули; дополняются нули справа.
                    if (leadingIncluded) digits = digits.PadRight(integers + decimals, '0');
                    coordinate = double.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, decimals);
                    if (value.StartsWith('-')) coordinate = -coordinate;
                }
                coordinate *= units!.Value;
                if (!double.IsFinite(coordinate)) throw Error("Некорректная координата.");
                if (match.Groups[1].Value == "X") x = coordinate + (incremental ? previous.X : 0);
                else y = coordinate + (incremental ? previous.Y : 0);
            }
            return new PointMm(x, y);
        }
    }

    private static DxfContour Slot(PointMm start, PointMm end, double diameter)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return new DxfCircle("DRILL", start, diameter / 2);
        var nx = -dy / length * diameter / 2; var ny = dx / length * diameter / 2;
        return new DxfPolyline("DRILL_SLOTS",
            [new(start.X + nx, start.Y + ny), new(end.X + nx, end.Y + ny),
                new(end.X - nx, end.Y - ny), new(start.X - nx, start.Y - ny)], true, [0, -1, 0, -1]);
    }

    [GeneratedRegex(@"^;FILE_FORMAT=(\d+):(\d+)$")]
    private static partial Regex FileFormat();
    [GeneratedRegex(@"^(METRIC|INCH)(?:,(LZ|TZ))?(?:,(0+)\.(0+))?$")]
    private static partial Regex UnitDeclaration();
    [GeneratedRegex(@"^T(\d+)(?:C(\d+(?:\.\d+)?))?$")]
    private static partial Regex ToolCommand();
    [GeneratedRegex(@"([XY])([+-]?(?:\d+(?:\.\d*)?|\.\d+))")]
    private static partial Regex Coordinates();
}
