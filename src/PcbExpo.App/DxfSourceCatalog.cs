using PcbExpo.Core;

namespace PcbExpo.App;

internal sealed record DxfSource(string Label, string FileStem, string Description, Func<IReadOnlyList<DxfContour>> Build,
    RectMm? ViewBounds = null)
{
    public override string ToString() => Label;
}

internal sealed class DxfSourceCatalog(ExposureRasterService rasterService)
{
    public IReadOnlyList<DxfSource> Create(ProjectModel project, GerberPackage? package, PrinterInfo? printerInfo, bool cncFirst = false)
    {
        var contours = new ContourService();
        var sources = new List<DxfSource>
        {
            new("Заготовка и механические отверстия", "blank",
                "Прямоугольник заготовки и четыре окружности отверстий с точными размерами профиля. Начало координат — левый нижний угол заготовки.",
                () => contours.Blank(project.Blank)),
            new("Точки центровки", "registration",
                "Пять окружностей с диаметром и координатами светящихся точек из профиля. Начало координат — левый нижний угол заготовки.",
                () => contours.Registration(project.Blank)),
            new("Калибровочный рисунок", "calibration",
                "Открытая линия 100 мм и замкнутый квадрат 50 × 50 мм с точными эталонными размерами. Начало координат — левый нижний угол заготовки.",
                () => contours.Calibration(project.Blank))
        };
        if (package is not null && project.LayerPaths.TryGetValue(GerberLayerKind.BoardOutline, out var outlinePath))
            sources.Add(new("Контур платы (линии Gerber)", "board_outline",
                "Осевые линии и дуги выбранного контура платы, без толщины апертуры. Координаты исходного Gerber; размещение и переворот не применяются.",
                () => new GerberOutlineService().Read(outlinePath)));
        if (printerInfo is { } printer)
            sources.Add(new($"Текущая экспозиция: {UiText.Exposure(project.Mode)}", $"exposure_{project.Mode}",
                $"Границы белых областей финальной маски с текущими преобразованиями, компенсацией и размещением плат. Начало координат — левый нижний угол заготовки. Точность ограничена шагом LCD: X {printer.PixelPitchXmm:F6}, Y {printer.PixelPitchYmm:F6} мм.",
                () =>
                {
                    var raster = RasterGeometry.Native(printer);
                    using var mask = rasterService.Build(project, printer, raster, package?.BoardBoundsMm);
                    return contours.FromMask(mask.Image, raster, new PointMm(-mask.BlankOnLcd.X, -mask.BlankOnLcd.Y));
                }));
        if (package is not null)
        {
            var blankBounds = new RectMm(0, 0, project.Blank.WidthMm, project.Blank.HeightMm);
            var side = project.IsBottom ? "Bottom" : "Top";
            var drillSelection = project.LayerPaths.TryGetValue(GerberLayerKind.Drill, out var selectedDrill)
                ? Path.GetFileName(selectedDrill) : "все файлы сверловки";
            var cncDescription = $"Вся раскладка плат, сторона {side}; {drillSelection}. Начало координат — левый нижний угол заготовки, X вправо, Y вверх, мм. Учитываются размещение копий, Bottom и пользовательские зеркалирования текущего режима платы. Оптическое зеркалирование CXDLPV4, инверсия и компенсация экспозиции не применяются. Контуры — осевые линии Gerber; отверстия — окружности исходного диаметра, прямые пазы — замкнутые контуры с дугами. При режиме калибровки используется сторона Top без пользовательских зеркалирований. Траектории инструмента задаются в CAM.";
            sources.Add(new("CNC: контуры плат и сверловка всей раскладки", "cnc_layout",
                cncDescription, () => new CncLayoutService().Build(project, package), blankBounds));
            sources.Add(new("CNC: только сверловка всей раскладки", "cnc_drills",
                cncDescription, () => new CncLayoutService().Build(project, package, includeOutline: false), blankBounds));
            sources.Add(new("CNC: только контуры плат всей раскладки", "cnc_outlines",
                cncDescription, () => new CncLayoutService().Build(project, package, includeDrills: false), blankBounds));
            foreach (var drillLayer in package.Layers.Where(l => l.Kind == GerberLayerKind.Drill))
                sources.Add(new($"Сверловка: {drillLayer.RelativePath} (исходные координаты)", UiText.SafeFileName(Path.GetFileNameWithoutExtension(drillLayer.Name)),
                    "Отверстия и прямые пазы одного Excellon без размещения на заготовке. Единицы преобразуются в мм, диаметры сохраняются. Для обработки размещённых копий выберите источник CNC.",
                    () => new ExcellonDrillService().Read(drillLayer.Path)));
            foreach (var layer in package.Layers.Where(l => l.Kind != GerberLayerKind.Drill))
                sources.Add(new($"Gerber: {layer.RelativePath}", UiText.SafeFileName(Path.GetFileNameWithoutExtension(layer.Name)),
                    "Внешние и внутренние границы рисунка слоя, включая толщину линий и апертуры. Координаты исходного Gerber; размещение и преобразования экспозиции не применяются. Контуры получены из растра с шагом 0,01 мм.",
                    () => contours.Gerber(layer.Path)));
        }
        if (cncFirst)
        {
            var first = sources.Single(s => s.FileStem == "cnc_layout");
            sources.Remove(first); sources.Insert(0, first);
        }
        return sources;
    }
}
