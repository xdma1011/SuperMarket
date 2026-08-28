namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// نفس البرومبت بالضبط لكل المزوّدين الثلاثة (Gemini، Gemini Flash،
/// Claude) — الاختلاف بينهم آليات استدعاء الـAPI بس، لا محتوى الطلب. هذا
/// يضمن نتائج قابلة للمقارنة بغض النظر عن أي مزوّد فعليًا استجاب.
/// </summary>
public static class InvoiceOcrPrompt
{
    public const string Text = """
        أنت مساعد لاستخراج بيانات من صور فواتير شراء لسوبر ماركت. الفاتورة ممكن تكون بالعربي أو الإنجليزي أو مختلطة.

        استخرج المعلومات التالية من الصورة المرفقة، وأعد النتيجة بصيغة JSON فقط، بدون أي نص إضافي قبلها أو بعدها، مطابقة تمامًا لهذا الشكل:

        {
          "supplierName": "اسم المورد كما يظهر بالفاتورة، أو null إذا غير واضح",
          "supplierInvoiceReference": "رقم/مرجع الفاتورة عند المورد، أو null",
          "invoiceDate": "تاريخ الفاتورة بصيغة YYYY-MM-DD، أو null إذا غير واضح",
          "currency": "رمز العملة إذا ظهر (مثل JOD)، أو null",
          "items": [
            {
              "rawProductName": "اسم الصنف كما هو مكتوب بالفاتورة بالضبط",
              "quantity": <رقم>,
              "unitOfMeasure": "الوحدة كما هي مكتوبة (كرتونة، حبة، كيلو...) أو null",
              "unitCost": <سعر الوحدة قبل الضريبة إن أمكن تمييزه، وإلا كما ظهر>,
              "lineTotal": <إجمالي السطر إذا ظهر، وإلا null>
            }
          ],
          "invoiceTotal": <الإجمالي الكلي، وإلا null>,
          "extractionConfidence": "high" | "medium" | "low",
          "warnings": ["أي جزء غير واضح أو صعب القراءة، بالعربي"]
        }

        قواعد صارمة:
        - لا تخترع أي رقم أو اسم غير موجود فعليًا بالصورة. لو غير واضح، استخدم null وأضف ملاحظة بـwarnings.
        - لا تحوّل الوحدات ولا تحسب أسعار غير مكتوبة صراحة.
        - أعد JSON صالح فقط، بدون markdown code fences، بدون أي شرح خارج الـJSON.
        """;
}

/// <summary>
/// مفاتيح إعدادات الذكاء الاصطناعي. مفتاح API فاضي = المزوّد معطّل، فشل
/// هادئ (نفس مبدأ Telegram — مفتاح فاضي مش خطأ إعداد يوقف شيء).
///
/// أسماء الموديلات قابلة للتعديل من الإعدادات عمدًا، لا مثبَّتة بالكود —
/// مزوّدو الذكاء الاصطناعي بيغيّروا أسماء الموديلات بمعدل سريع، وتغيير
/// إعداد أرخص بكثير من تعديل كود ونشر جديد. القيم الافتراضية:
/// "gemini-pro-latest"/"gemini-flash-latest" (alias دائم بيحافظ عليه
/// Google نفسه ليشير لآخر إصدار تلقائيًا).
/// </summary>
public static class InvoiceOcrSettingsKeys
{
    public const string GeminiApiKey = "Ai.GeminiApiKey";
    public const string GeminiProModelName = "Ai.GeminiProModelName";
    public const string GeminiFlashModelName = "Ai.GeminiFlashModelName";
    public const string ClaudeApiKey = "Ai.ClaudeApiKey";
    public const string ClaudeModelName = "Ai.ClaudeModelName";
}
