using ContentEmpireSystem.Core.DTOs;
using ContentEmpireSystem.Core.Enums;

namespace ContentEmpireSystem.Core;

/// <summary>
/// User-facing error text + soft hints (إشارات) kept in sync with the dashboard.
/// </summary>
public static class OperationHints
{
    public const string LogsFolderRelative = "logs/operations";

    public static string TodayLogFileName() =>
        $"operations-{DateTime.UtcNow:yyyy-MM-dd}.log";

    public static string LogHintText() =>
        $"للتشخيص: راجع {LogsFolderRelative}/{TodayLogFileName()}";

    public static ApiErrorPayload ForOperationKind(ProjectOperationKind kind, string scenario = "busy") =>
        scenario switch
        {
            "queued" => Queued(Describe(kind)),
            "queueFull" => QueueFull(Describe(kind)),
            _ => AlreadyRunning(Describe(kind))
        };

    public static string Describe(ProjectOperationKind kind) => kind switch
    {
        ProjectOperationKind.IdeaGeneration => "توليد الفكرة",
        ProjectOperationKind.ScriptGeneration => "توليد السكريبت",
        ProjectOperationKind.ImageGeneration => "توليد الصور (FLUX)",
        ProjectOperationKind.VideoAnimation => "تحريك الصور (Wan TI2V)",
        ProjectOperationKind.VideoGeneration => "توليد فيديو (Wan T2V)",
        ProjectOperationKind.AudioGeneration => "توليد الصوت (TTS)",
        ProjectOperationKind.Production => "تركيب FFmpeg",
        ProjectOperationKind.VoiceCodeMediaUpload => "رفع وسائط Voice Code",
        _ => kind.ToString()
    };

    public static ApiErrorPayload AlreadyRunning(string operation) => new()
    {
        Error = $"{operation} ما زال قيد التنفيذ على هذا المشروع.",
        Hint = "انتظر حتى تظهر «جاهز» في شريط الحالة، أو تابع لوحة النشاط.",
        Operation = operation,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload Queued(string operation) => new()
    {
        Error = $"بدأ {operation} في الخلفية.",
        Hint = "تابع التقدّم من شريط الحالة ولوحة النشاط — لا تغلق التبويب.",
        Operation = operation,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload QueueFull(string operation) => new()
    {
        Error = $"قائمة انتظار {operation} ممتلئة حالياً.",
        Hint = "انتظر دقيقة ثم أعد المحاولة، أو أوقف عملية أخرى على نفس المشروع.",
        Operation = operation,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload FrontendLock(string? activeOperation) => new()
    {
        Error = activeOperation is null or ""
            ? "عملية ثقيلة أخرى ما زالت قيد التنفيذ في هذا التبويب."
            : $"«{activeOperation}» ما زالت قيد التنفيذ في هذا التبويب.",
        Hint = "انتظر حتى يظهر «جاهز» في شريط الحالة. إن استمرّت المشكلة، حدّث الصفحة بعد انتهاء العملية.",
        Operation = activeOperation,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload WorkflowBlocked(string error, ProjectOperationKind? attempted = null) => new()
    {
        Error = error,
        Hint = attempted is null
            ? "راجع الخطوة الحالية في مسار الإنتاج ثم أعد المحاولة."
            : $"الخطوة المطلوبة الآن: {Describe(attempted.Value)} — أكمل المتطلبات السابقة أولاً.",
        Operation = attempted is null ? null : Describe(attempted.Value),
        LogHint = LogHintText()
    };

    public static ApiErrorPayload ProjectNotFound() => new()
    {
        Error = "المشروع غير موجود أو حُذف.",
        Hint = "حدّث قائمة المشاريع أو اختر مشروعاً آخر.",
        LogHint = LogHintText()
    };

    public static ApiErrorPayload ConnectionLost() => new()
    {
        Error = "انقطع الاتصال قبل انتهاء العملية.",
        Hint = "قد تكون العملية ما زالت تعمل في الخلفية — راجع لوحة النشاط قبل إعادة المحاولة.",
        LogHint = LogHintText()
    };

    public static ApiErrorPayload InternalError(string? correlationId = null) => new()
    {
        Error = "حدث خطأ داخلي غير متوقع.",
        Hint = correlationId is null
            ? "أعد المحاولة بعد قليل. إن تكرّر، أرسل ملف السجل اليومي."
            : $"أعد المحاولة. معرّف الطلب: {correlationId} — أرفقه مع ملف السجل.",
        CorrelationId = correlationId,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload RateLimited() => new()
    {
        Error = "طلبات كثيرة في وقت قصير.",
        Hint = "انتظر 10–20 ثانية ثم أعد المحاولة.",
        LogHint = LogHintText()
    };

    public static ApiErrorPayload WaitTimeout(string operation) => new()
    {
        Error = $"انتهت مهلة انتظار {operation}.",
        Hint = "راجع لوحة النشاط — قد تكون العملية ما زالت تعمل. إن ظهر «فشل»، أعد المحاولة.",
        Operation = operation,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload RequestTimeout(string profileHint) => new()
    {
        Error = "انتهت مهلة الطلب قبل وصول رد من الخادم.",
        Hint = profileHint,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload GpuBusy(string? currentWorkload = null) => new()
    {
        Error = currentWorkload is null
            ? "معالج الرسوميات (GPU) مشغول بعملية أخرى."
            : $"GPU مشغول حالياً: {currentWorkload}.",
        Hint = "انتظر حتى يظهر «GPU: جاهز» في الشريط العلوي، أو أوقف ComfyUI/LM Studio ثم أعد المحاولة.",
        Operation = currentWorkload,
        LogHint = LogHintText()
    };

    public static ApiErrorPayload ComfyVramCrash() => new()
    {
        Error = "ComfyUI توقف أثناء التوليد — غالباً بسبب VRAM.",
        Hint = "أعد تشغيل Comfy Desktop، تأكد أن اللاب موصول بالشاحن، ثم أعد المحاولة.",
        Operation = "ComfyUI / Wan",
        LogHint = LogHintText()
    };

    public static ApiErrorPayload ComfyUnavailable() => new()
    {
        Error = "ComfyUI غير متاح على هذا الجهاز.",
        Hint = "شغّل ComfyUI Desktop وتأكد أن http://127.0.0.1:8188 يفتح في المتصفح، ثم أعد المحاولة.",
        Operation = "ComfyUI",
        LogHint = LogHintText()
    };

    public static ApiErrorPayload FromExceptionMessage(string message, string? correlationId = null)
    {
        if (IsComfyConnectionFailure(message))
            return ComfyUnavailable();

        if (message.Contains("ComfyUI غير متاح", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ComfyUI توقف بعد", StringComparison.OrdinalIgnoreCase))
            return ComfyUnavailable();

        if (message.Contains("GPU مشغول", StringComparison.OrdinalIgnoreCase)
            || message.Contains("الـ GPU مشغول", StringComparison.OrdinalIgnoreCase)
            || message.Contains("GPU", StringComparison.OrdinalIgnoreCase) && message.Contains("5 دقائ", StringComparison.Ordinal))
            return GpuBusy();

        if (message.Contains("ComfyUI", StringComparison.OrdinalIgnoreCase)
            && (message.Contains("crash", StringComparison.OrdinalIgnoreCase)
                || message.Contains("VRAM", StringComparison.OrdinalIgnoreCase)
                || message.Contains("توقف", StringComparison.OrdinalIgnoreCase)))
            return ComfyVramCrash();

        if (message.Contains("المشروع مشغول", StringComparison.OrdinalIgnoreCase))
            return new ApiErrorPayload
            {
                Error = message,
                Hint = "انتظر انتهاء العملية الجارية على هذا المشروع.",
                LogHint = LogHintText(),
                CorrelationId = correlationId
            };

        if (message.Contains("ولّد", StringComparison.OrdinalIgnoreCase)
            || message.Contains("أكمل", StringComparison.OrdinalIgnoreCase)
            || message.Contains("اكتب", StringComparison.OrdinalIgnoreCase))
            return new ApiErrorPayload
            {
                Error = message,
                Hint = "أكمل الخطوة المطلوبة في مسار الإنتاج ثم أعد المحاولة.",
                LogHint = LogHintText(),
                CorrelationId = correlationId
            };

        return new ApiErrorPayload
        {
            Error = message,
            Hint = "راجع لوحة النشاط وملف السجل اليومي للتفاصيل.",
            LogHint = LogHintText(),
            CorrelationId = correlationId
        };
    }

    private static bool IsComfyConnectionFailure(string message) =>
        message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
        || message.Contains("127.0.0.1:8188", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ComfyUI غير متاح", StringComparison.OrdinalIgnoreCase)
        || (message.Contains("8188", StringComparison.OrdinalIgnoreCase)
            && message.Contains("connection", StringComparison.OrdinalIgnoreCase));
}
