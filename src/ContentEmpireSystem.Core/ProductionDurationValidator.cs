namespace ContentEmpireSystem.Core;

public static class ProductionDurationValidator
{
    public static bool ExceedsMaxDuration(double durationSec, int maxVideoDurationSec) =>
        maxVideoDurationSec > 0 && durationSec > maxVideoDurationSec + 0.5;

    public static string FormatExceedsError(double durationSec, int maxSec) =>
        $"مدة الصوت {durationSec:F0} ثانية تتجاوز الحد الأقصى ({maxSec} ثانية). قصّر السكريبت أو عدّل الإعداد.";
}
