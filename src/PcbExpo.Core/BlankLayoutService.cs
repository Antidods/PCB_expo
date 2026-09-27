namespace PcbExpo.Core;

public sealed class BlankLayoutService
{
    public IReadOnlyList<PointMm> MechanicalHoles(BlankProfile blank)
    {
        if (blank.RegistrationHoleDiameterMm <= 0 || blank.HoleClearanceMm < 0 ||
            blank.HoleInsetXmm <= blank.RegistrationHoleDiameterMm / 2 ||
            blank.HoleInsetYmm <= blank.RegistrationHoleDiameterMm / 2 ||
            blank.HoleInsetXmm >= blank.WidthMm / 2 || blank.HoleInsetYmm >= blank.HeightMm / 2)
            throw new InvalidOperationException("Проверьте диаметр и отступы механических отверстий.");
        return
        [
            new(blank.HoleInsetXmm, blank.HoleInsetYmm),
            new(blank.WidthMm - blank.HoleInsetXmm, blank.HoleInsetYmm),
            new(blank.HoleInsetXmm, blank.HeightMm - blank.HoleInsetYmm),
            new(blank.WidthMm - blank.HoleInsetXmm, blank.HeightMm - blank.HoleInsetYmm)
        ];
    }

    public IReadOnlyList<PointMm> AlignmentPoints(BlankProfile blank)
    {
        if (blank.CustomAlignmentPoints is { Count: 5 } custom)
        {
            if (custom.Any(p => p.X <= 0 || p.Y <= 0 || p.X >= blank.WidthMm || p.Y >= blank.HeightMm))
                throw new InvalidOperationException("Точки центровки должны находиться внутри заготовки.");
            return custom;
        }
        if (blank.CustomAlignmentPoints is not null)
            throw new InvalidOperationException("Custom alignment coordinates должны содержать ровно 5 точек.");
        if (blank.AlignmentPointDiameterMm <= 0 ||
            blank.AlignmentInsetXmm <= 0 || blank.AlignmentInsetYmm <= 0 ||
            blank.AlignmentInsetXmm >= blank.WidthMm / 2 || blank.AlignmentInsetYmm >= blank.HeightMm / 2)
            throw new InvalidOperationException("Проверьте диаметр и отступы светящихся точек.");
        return
        [
            new(blank.WidthMm / 2, blank.HeightMm / 2),
            new(blank.AlignmentInsetXmm, blank.HeightMm - blank.AlignmentInsetYmm),
            new(blank.WidthMm - blank.AlignmentInsetXmm, blank.HeightMm - blank.AlignmentInsetYmm),
            new(blank.AlignmentInsetXmm, blank.AlignmentInsetYmm),
            new(blank.WidthMm - blank.AlignmentInsetXmm, blank.AlignmentInsetYmm)
        ];
    }

    public bool IntersectsHoleExclusion(RectMm board, BlankProfile blank)
    {
        var radius = blank.RegistrationHoleDiameterMm / 2 + blank.HoleClearanceMm;
        foreach (var hole in MechanicalHoles(blank))
        {
            var closestX = Math.Clamp(hole.X, board.X, board.Right);
            var closestY = Math.Clamp(hole.Y, board.Y, board.Top);
            var dx = hole.X - closestX;
            var dy = hole.Y - closestY;
            if (dx * dx + dy * dy <= radius * radius) return true;
        }
        return false;
    }
}

public sealed class PanelizationService(BlankLayoutService layout)
{
    public IReadOnlyList<RectMm> Layout(double boardWidthMm, double boardHeightMm,
        BlankProfile blank, PointMm singlePosition, PanelizationSettings settings)
    {
        if (boardWidthMm <= 0 || boardHeightMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(boardWidthMm));
        if (settings.SpacingXmm < 0 || settings.SpacingYmm < 0 ||
            settings.MarginLeftMm < 0 || settings.MarginRightMm < 0 ||
            settings.MarginBottomMm < 0 || settings.MarginTopMm < 0)
            throw new InvalidOperationException("Отступы и интервалы должны быть неотрицательными.");

        if (settings.Mode == PlacementMode.Single)
        {
            var single = new RectMm(singlePosition.X, singlePosition.Y, boardWidthMm, boardHeightMm);
            Validate(single, blank, settings);
            return [single];
        }

        var availableWidth = blank.WidthMm - settings.MarginLeftMm - settings.MarginRightMm;
        var availableHeight = blank.HeightMm - settings.MarginBottomMm - settings.MarginTopMm;
        var columns = (int)Math.Floor((availableWidth + settings.SpacingXmm) / (boardWidthMm + settings.SpacingXmm) + 1e-9);
        var rows = (int)Math.Floor((availableHeight + settings.SpacingYmm) / (boardHeightMm + settings.SpacingYmm) + 1e-9);
        if (columns <= 0 || rows <= 0) throw new InvalidOperationException("Ни одна плата не помещается на заготовке.");
        var boards = new List<RectMm>(columns * rows);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var board = new RectMm(settings.MarginLeftMm + column * (boardWidthMm + settings.SpacingXmm),
                settings.MarginBottomMm + row * (boardHeightMm + settings.SpacingYmm),
                boardWidthMm, boardHeightMm);
            if (!layout.IntersectsHoleExclusion(board, blank)) boards.Add(board);
        }
        if (boards.Count == 0) throw new InvalidOperationException("Все позиции пересекают запретные области отверстий.");
        return boards;
    }

    private void Validate(RectMm board, BlankProfile blank, PanelizationSettings settings)
    {
        if (board.X < settings.MarginLeftMm || board.Y < settings.MarginBottomMm ||
            board.Right > blank.WidthMm - settings.MarginRightMm ||
            board.Top > blank.HeightMm - settings.MarginTopMm)
            throw new InvalidOperationException("PCB выходит за допустимую область заготовки.");
        if (layout.IntersectsHoleExclusion(board, blank))
            throw new InvalidOperationException("PCB пересекает запретную область механического отверстия.");
    }
}
