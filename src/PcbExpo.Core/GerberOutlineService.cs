using System.Globalization;
using System.Text.RegularExpressions;

namespace PcbExpo.Core;

/// <summary>Reads outline centerlines without aperture widths. Complex artwork uses ContourService.Gerber.</summary>
public sealed partial class GerberOutlineService
{
    public IReadOnlyList<DxfContour> Read(string path)
    {
        var result = new List<DxfContour>();
        var points = new List<PointMm>();
        var bulges = new List<double>();
        var current = new PointMm(0, 0);
        var operation = 2;
        var interpolation = 1;
        var relative = false;
        var trailingZeros = false;
        var singleQuadrant = false;
        var units = 1.0;
        var formatSet = false;
        var unitsSet = false;
        var xIntegers = 0; var xDecimals = 0;
        var yIntegers = 0; var yDecimals = 0;

        foreach (Match commandMatch in Commands().Matches(File.ReadAllText(path)))
        {
            var command = Whitespace().Replace(commandMatch.Value, "").Trim('*');
            if (command.StartsWith("G04", StringComparison.Ordinal)) continue;
            if (command.StartsWith('%'))
            {
                var format = Format().Match(command);
                if (format.Success)
                {
                    trailingZeros = format.Groups[1].Value == "T";
                    relative = format.Groups[2].Value == "I";
                    xIntegers = int.Parse(format.Groups[3].Value); xDecimals = int.Parse(format.Groups[4].Value);
                    yIntegers = int.Parse(format.Groups[5].Value); yDecimals = int.Parse(format.Groups[6].Value);
                    formatSet = true;
                }
                else if (command.StartsWith("%MOMM", StringComparison.Ordinal)) { units = 1; unitsSet = true; }
                else if (command.StartsWith("%MOIN", StringComparison.Ordinal)) { units = 25.4; unitsSet = true; }
                else if (command.StartsWith("%LPC", StringComparison.Ordinal) ||
                    new[] { "%SR", "%LM", "%LR", "%LS", "%OF", "%SF", "%AS", "%MI", "%IP" }
                        .Any(prefix => command.StartsWith(prefix, StringComparison.Ordinal)))
                    throw Unsupported();
                else if (!(command.StartsWith("%ADD", StringComparison.Ordinal) || command.StartsWith("%AM", StringComparison.Ordinal) ||
                    command.StartsWith("%LPD", StringComparison.Ordinal) || command.StartsWith("%TF", StringComparison.Ordinal) ||
                    command.StartsWith("%TA", StringComparison.Ordinal) || command.StartsWith("%TO", StringComparison.Ordinal) ||
                    command.StartsWith("%TD", StringComparison.Ordinal)))
                    throw Unsupported();
                continue;
            }
            if (command.StartsWith("M02", StringComparison.Ordinal)) break;
            foreach (Match code in GCodes().Matches(command))
            {
                switch (int.Parse(code.Groups[1].Value))
                {
                    case 1: case 2: case 3: interpolation = int.Parse(code.Groups[1].Value); break;
                    case 36: case 37: Flush(); break;
                    case 74: singleQuadrant = true; break;
                    case 75: singleQuadrant = false; break;
                    case 90: relative = false; break;
                    case 91: relative = true; break;
                    case 70: units = 25.4; unitsSet = true; break;
                    case 71: units = 1; unitsSet = true; break;
                    case 54: break;
                    default: throw Unsupported();
                }
            }
            var fields = Fields().Matches(command).Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            if (fields.TryGetValue("D", out var d))
            {
                var code = int.Parse(d, CultureInfo.InvariantCulture);
                if (code is >= 1 and <= 3) operation = code;
            }
            if (!fields.ContainsKey("X") && !fields.ContainsKey("Y") && !fields.ContainsKey("I") && !fields.ContainsKey("J")) continue;
            if (!formatSet || !unitsSet) throw Unsupported();
            var next = new PointMm(Coordinate("X", current.X, xIntegers, xDecimals),
                Coordinate("Y", current.Y, yIntegers, yDecimals));
            switch (operation)
            {
                case 2: Flush(); break;
                case 3: throw Unsupported();
                case 1:
                    if (points.Count == 0) { points.Add(current); bulges.Add(0); }
                    if (interpolation == 1) Add(next, 0);
                    else
                    {
                        if (singleQuadrant) throw Unsupported();
                        var center = new PointMm(current.X + Offset("I", xIntegers, xDecimals),
                            current.Y + Offset("J", yIntegers, yDecimals));
                        var radius = Math.Sqrt(Math.Pow(current.X - center.X, 2) + Math.Pow(current.Y - center.Y, 2));
                        var endRadius = Math.Sqrt(Math.Pow(next.X - center.X, 2) + Math.Pow(next.Y - center.Y, 2));
                        if (radius <= 0 || Math.Abs(radius - endRadius) > Math.Max(0.001, radius * 0.0001)) throw Unsupported();
                        var startAngle = Math.Atan2(current.Y - center.Y, current.X - center.X);
                        var sweep = Math.Atan2(next.Y - center.Y, next.X - center.X) - startAngle;
                        if (interpolation == 3 && sweep <= 0) sweep += Math.Tau;
                        if (interpolation == 2 && sweep >= 0) sweep -= Math.Tau;
                        // Split arcs into at most 90 degrees, including a full circle, for portable DXF polylines.
                        var segments = (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2));
                        for (var i = 1; i <= segments; i++)
                        {
                            var angle = startAngle + sweep * i / segments;
                            Add(i == segments ? next : new PointMm(center.X + radius * Math.Cos(angle),
                                center.Y + radius * Math.Sin(angle)), Math.Tan(sweep / segments / 4));
                        }
                    }
                    break;
            }
            current = next;

            double Coordinate(string key, double previous, int integers, int decimals) => fields.TryGetValue(key, out var value)
                ? Parse(value, integers, decimals) + (relative ? previous : 0) : previous;
            double Offset(string key, int integers, int decimals) => fields.TryGetValue(key, out var value) ? Parse(value, integers, decimals) : 0;
        }
        Flush();
        if (result.Count == 0) throw new InvalidOperationException("В Gerber нет линий контура. Выберите границы рисунка слоя Gerber.");
        return result;

        double Parse(string value, int integers, int decimals)
        {
            if (value.Contains('.')) return double.Parse(value, CultureInfo.InvariantCulture) * units;
            var sign = value.StartsWith('-') ? -1 : 1;
            var digits = value.TrimStart('+', '-');
            if (trailingZeros) digits = digits.PadRight(integers + decimals, '0');
            return sign * double.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, decimals) * units;
        }
        void Add(PointMm point, double bulge)
        {
            if (point == points[^1]) return;
            bulges[^1] = bulge;
            points.Add(point); bulges.Add(0);
        }
        void Flush()
        {
            if (points.Count >= 2)
            {
                var closed = points.Count > 2 && points[0] == points[^1];
                if (closed) { points.RemoveAt(points.Count - 1); bulges.RemoveAt(bulges.Count - 1); }
                result.Add(new DxfPolyline("BOARD_OUTLINE", points.ToArray(), closed, bulges.ToArray()));
            }
            points.Clear(); bulges.Clear();
        }
    }

    private static InvalidOperationException Unsupported() => new(
        "Этот Gerber нельзя экспортировать как осевой контур. Выберите его в списке слоёв DXF для экспорта границ рисунка.");

    [GeneratedRegex(@"%[^%]*%|[^%]*?\*", RegexOptions.Singleline)]
    private static partial Regex Commands();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"^%FS([LT])([AI])X(\d)(\d)Y(\d)(\d)\*%$")]
    private static partial Regex Format();
    [GeneratedRegex(@"G(\d+)")]
    private static partial Regex GCodes();
    [GeneratedRegex(@"([XYIJD])([+-]?\d+(?:\.\d+)?)")]
    private static partial Regex Fields();
}
