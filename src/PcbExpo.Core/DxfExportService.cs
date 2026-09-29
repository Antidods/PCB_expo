using System.Globalization;
using System.Text;

namespace PcbExpo.Core;

public abstract record DxfContour(string Layer);
public sealed record DxfPolyline(string Layer, IReadOnlyList<PointMm> Points, bool Closed,
    IReadOnlyList<double>? Bulges = null) : DxfContour(Layer);
public sealed record DxfCircle(string Layer, PointMm Center, double RadiusMm) : DxfContour(Layer);

public sealed class DxfExportService
{
    public void Export(string path, IReadOnlyList<DxfContour> contours)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (contours.Count == 0) throw new InvalidOperationException("Нет контуров для экспорта.");
        foreach (var contour in contours) Validate(contour);
        // Generate first so a geometry error cannot truncate an existing file.
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        Write(text, contours);
        File.WriteAllText(path, text.ToString(), Encoding.ASCII);
    }

    private static void Validate(DxfContour contour)
    {
        if (string.IsNullOrWhiteSpace(contour.Layer) ||
            contour.Layer.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Некорректное имя слоя DXF.");
        switch (contour)
        {
            case DxfPolyline line when line.Points.Count >= (line.Closed ? 3 : 2) &&
                line.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)) &&
                (line.Bulges is null || line.Bulges.Count == line.Points.Count && line.Bulges.All(double.IsFinite)):
                return;
            case DxfCircle circle when double.IsFinite(circle.Center.X) && double.IsFinite(circle.Center.Y) &&
                double.IsFinite(circle.RadiusMm) && circle.RadiusMm > 0:
                return;
            default:
                throw new InvalidOperationException("Контур DXF должен содержать конечные координаты и ненулевую геометрию.");
        }
    }

    private static void Write(TextWriter writer, IReadOnlyList<DxfContour> contours)
    {
        var nextHandle = 1;
        string Handle() => (nextHandle++).ToString("X", CultureInfo.InvariantCulture);
        void Pair(int code, object value)
        {
            writer.WriteLine(code.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine(value is double number ? number.ToString("0.############", CultureInfo.InvariantCulture) : value);
        }
        Pair(0, "SECTION"); Pair(2, "HEADER");
        Pair(9, "$ACADVER"); Pair(1, "AC1015");
        Pair(9, "$INSUNITS"); Pair(70, 4); // millimeters
        Pair(9, "$MEASUREMENT"); Pair(70, 1);
        Pair(0, "ENDSEC");
        Pair(0, "SECTION"); Pair(2, "TABLES");
        var lineTypeTable = Handle();
        Pair(0, "TABLE"); Pair(2, "LTYPE"); Pair(5, lineTypeTable); Pair(100, "AcDbSymbolTable"); Pair(70, 1);
        Pair(0, "LTYPE"); Pair(5, Handle()); Pair(330, lineTypeTable);
        Pair(100, "AcDbSymbolTableRecord"); Pair(100, "AcDbLinetypeTableRecord");
        Pair(2, "CONTINUOUS"); Pair(70, 0); Pair(3, "Solid line"); Pair(72, 65); Pair(73, 0); Pair(40, 0);
        Pair(0, "ENDTAB");
        var layers = contours.Select(c => c.Layer).Prepend("0").Distinct().ToArray();
        var layerTable = Handle();
        Pair(0, "TABLE"); Pair(2, "LAYER"); Pair(5, layerTable); Pair(100, "AcDbSymbolTable"); Pair(70, layers.Length);
        foreach (var layer in layers)
        {
            Pair(0, "LAYER"); Pair(5, Handle()); Pair(330, layerTable);
            Pair(100, "AcDbSymbolTableRecord"); Pair(100, "AcDbLayerTableRecord");
            Pair(2, layer); Pair(70, 0); Pair(62, 7); Pair(6, "CONTINUOUS");
        }
        Pair(0, "ENDTAB"); Pair(0, "ENDSEC");
        Pair(0, "SECTION"); Pair(2, "ENTITIES");
        foreach (var contour in contours)
        {
            Pair(0, contour is DxfCircle ? "CIRCLE" : "LWPOLYLINE");
            Pair(5, Handle());
            Pair(100, "AcDbEntity"); Pair(8, contour.Layer);
            switch (contour)
            {
                case DxfCircle circle:
                    Pair(100, "AcDbCircle"); Pair(10, circle.Center.X); Pair(20, circle.Center.Y);
                    Pair(30, 0); Pair(40, circle.RadiusMm);
                    break;
                case DxfPolyline line:
                    Pair(100, "AcDbPolyline"); Pair(90, line.Points.Count); Pair(70, line.Closed ? 1 : 0);
                    for (var i = 0; i < line.Points.Count; i++)
                    {
                        Pair(10, line.Points[i].X); Pair(20, line.Points[i].Y);
                        if (line.Bulges is { } bulges && bulges[i] != 0) Pair(42, bulges[i]);
                    }
                    break;
            }
        }
        Pair(0, "ENDSEC"); Pair(0, "EOF");
    }
}
