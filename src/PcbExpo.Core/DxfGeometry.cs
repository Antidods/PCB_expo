namespace PcbExpo.Core;

/// <summary>Точки для просмотра и проверки габаритов. DXF сохраняет исходные окружности и дуги.</summary>
public static class DxfGeometry
{
    public static IReadOnlyList<PointMm> Points(DxfContour contour)
    {
        if (contour is DxfCircle circle)
            return Enumerable.Range(0, 180).Select(i => new PointMm(
                circle.Center.X + circle.RadiusMm * Math.Cos(Math.Tau * i / 180),
                circle.Center.Y + circle.RadiusMm * Math.Sin(Math.Tau * i / 180))).ToArray();
        if (contour is not DxfPolyline line) throw new ArgumentException("Неподдерживаемый контур.");
        var result = new List<PointMm>();
        var segments = line.Closed ? line.Points.Count : line.Points.Count - 1;
        for (var i = 0; i < segments; i++)
        {
            var a = line.Points[i]; var b = line.Points[(i + 1) % line.Points.Count];
            result.Add(a);
            var bulge = line.Bulges?[i] ?? 0;
            if (bulge == 0) continue;
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var offset = (1 - bulge * bulge) / (4 * bulge);
            var center = new PointMm((a.X + b.X) / 2 - dy * offset, (a.Y + b.Y) / 2 + dx * offset);
            var radius = Math.Sqrt(Math.Pow(a.X - center.X, 2) + Math.Pow(a.Y - center.Y, 2));
            var start = Math.Atan2(a.Y - center.Y, a.X - center.X);
            var sweep = 4 * Math.Atan(bulge);
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 90)));
            var fractions = Enumerable.Range(1, steps - 1).Select(k => (double)k / steps).ToList();
            // Добавляем точные экстремумы дуг, чтобы проверка габаритов не зависела от шага preview.
            for (var k = 0; k < 4; k++)
            {
                var angle = k * Math.PI / 2;
                var delta = sweep > 0 ? (angle - start + Math.Tau) % Math.Tau : (start - angle + Math.Tau) % Math.Tau;
                if (delta > 0 && delta < Math.Abs(sweep)) fractions.Add(delta / Math.Abs(sweep));
            }
            foreach (var fraction in fractions.Order())
            {
                var angle = start + sweep * fraction;
                result.Add(new PointMm(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
            }
        }
        if (!line.Closed) result.Add(line.Points[^1]);
        return result;
    }
}
