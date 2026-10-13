namespace ContentEmpireSystem.Core.Enums;



public enum VideoProjectStatus

{

    Draft = 0,

    IdeaGenerated = 1,

    IdeaConfirmed = 2,

    ScriptGenerated = 3,

    // 4–5 و 8: حالات موافقة قديمة — لم تعد مستخدمة (تُرحَّل تلقائياً عند التشغيل)

    Processing = 6,

    Published = 7,

    Failed = 9,

}

