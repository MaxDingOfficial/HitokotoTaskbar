namespace HitokotoTaskbar.Core;

/// <summary>
/// Selects a verified empty interval before the actual Start button. All rectangles are
/// physical pixels, never percentages or guessed icon widths. Empty means hide the overlay.
/// </summary>
public static class TaskbarGapRules
{
    public static PixelRect FindBeforeStart(PixelRect bar, int startLeft, IEnumerable<PixelRect> occupied, double scale)
    {
        ArgumentNullException.ThrowIfNull(occupied);
        if (bar.IsEmpty || startLeft < bar.Left || startLeft > bar.Right ||
            !double.IsFinite(scale) || scale <= 0 || scale > 10)
            return default;

        var padding = Math.Max(4, (int)Math.Ceiling(8 * scale));
        var rightLimit = Math.Max(bar.Left, startLeft - padding);
        var cursor = bar.Left;
        var best = default(PixelRect);
        var intervals = occupied.Where(rect => !rect.IsEmpty && rect.Top < bar.Bottom && rect.Bottom > bar.Top &&
                rect.Left < rightLimit && rect.Right > bar.Left)
            .Select(rect => new { Left = Math.Max(bar.Left, rect.Left), Right = Math.Min(bar.Right, rect.Right) })
            .OrderBy(rect => rect.Left);

        foreach (var interval in intervals)
        {
            if (interval.Right <= cursor) continue;
            var gapRight = Math.Max(cursor, Math.Min(rightLimit, interval.Left - padding));
            if (gapRight - cursor > best.Width) best = new(cursor, bar.Top, gapRight, bar.Bottom);
            cursor = Math.Max(cursor, Math.Min(rightLimit, interval.Right + padding));
        }
        if (rightLimit - cursor > best.Width) best = new(cursor, bar.Top, rightLimit, bar.Bottom);
        return best.Width >= Math.Ceiling(48 * scale) ? best : default;
    }
}
