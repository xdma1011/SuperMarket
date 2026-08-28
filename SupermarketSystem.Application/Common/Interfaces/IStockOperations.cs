using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// نتيجة محاولة خصم المخزون — ثلاث حالات، مش true/false بس.
/// السبب: بعد قرار "المخزون السالب مسموح"، ما عاد كافي نعرف "نجح ولا فشل"،
/// لازم نعرف كمان "هل صار الرصيد سالب بعد الخصم" — عشان نقدر نعلّم العملية
/// للمراجعة الإدارية لاحقًا بلا ما نوقف البيع.
/// </summary>
public enum StockDecrementOutcome
{
    /// <summary>الخصم نجح والرصيد ضل صفر أو أكتر — المسار الطبيعي.</summary>
    Succeeded,

    /// <summary>
    /// الخصم نجح بس الرصيد صار تحت الصفر. هذا بيصير بس لما إعداد
    /// Inventory.AllowNegativeStock يكون مفعّل — يعني قرار واعي ومقصود
    /// من صاحب النظام، مش خطأ. لازم تتعلّم العملية للمراجعة لاحقًا.
    /// </summary>
    SucceededWentNegative,

    /// <summary>
    /// الخصم فشل تمامًا — ما تغيّر أي شي بقاعدة البيانات. هذا بيصير بس
    /// لما إعداد AllowNegativeStock يكون مطفي (false)، وهو نفس السلوك
    /// الأصلي (المخزون السالب ممنوع افتراضيًا حسب Architecture Review).
    /// </summary>
    Failed
}

/// <summary>
/// عمليات المخزون اللي *لازم* ما تمر عبر النمط المعتاد
/// (تحميل الـentity → تعديله بالذاكرة → SaveChanges).
///
/// ليش هاد الـinterface أصلًا موجود، رغم إن التصميم بشكل عام بيرفض إضافة
/// abstractions بلا داعي حقيقي: صحة مسار البيع بتعتمد على إن الخصم يكون
/// جملة SQL واحدة ذرية (UPDATE شرطي)، مش تحميل وتعديل وحفظ. هذا شي
/// IApplicationDbContext (اللي شكله DbSet عادي) ما بيقدر يعبّر عنه. من غير
/// هاد الـseam، أي handler بالتطبيق كان رح يضطر يوصل مباشرة لـEF provider،
/// أو يرجع بصمت لنمط "اقرأ ثم اكتب" اللي فيه ثغرة تزامن حقيقية.
/// </summary>
public interface IStockOperations
{
    /// <summary>
    /// يحاول خصم كمية من المخزون بجملة SQL ذرية واحدة.
    ///
    /// المنطق (3 خطوات، بالترتيب):
    ///   1. يحاول الخصم العادي بشرط "الرصيد كافي" — نفس الآلية الأصلية.
    ///      لو نجح، بيرجع Succeeded. هذا المسار الشائع (99% من الحالات).
    ///   2. لو المخزون غير كافٍ، وallowNegative=false → يرجع Failed بلا
    ///      ما يغيّر أي شي (نفس السلوك الأصلي، لسه موجود ومحمي).
    ///   3. لو allowNegative=true → يعيد المحاولة بلا شرط الحد الأدنى
    ///      (يخصم حتى لو الرصيد رح يصير سالب). لو الصف أصلًا مش موجود
    ///      بقاعدة البيانات (المنتج ما انباع ولا اتشرى بهاد الفرع قط)،
    ///      بينشئ صف جديد برصيد سالب مباشرة، بجملة SQL ذرية كمان
    ///      (INSERT ... WHERE NOT EXISTS) تمنع تكرار الصف لو صار طلبين
    ///      متزامنين لنفس المنتج الجديد بنفس اللحظة بالضبط.
    ///
    /// كل خطوة من الثلاثة جملة SQL ذرية لحالها — الشرط والكتابة بيتنفذوا
    /// سوا بنفس الاستعلام، فبيعتان متزامنتان لآخر قطعة ما بيقدروا الاثنين
    /// ينجحوا: اللي بيوصل ثاني بيشوف الرصيد المُحدَّث أصلًا وما بيتغير شي.
    /// </summary>
    Task<StockDecrementOutcome> TryDecreaseAsync(
        Guid productId,
        Guid branchId,
        Guid? productBatchId,
        decimal quantityBase,
        bool allowNegative,
        CancellationToken cancellationToken);
}

/// <summary>
/// عمليات على فواتير البيع لازم تكون جملة SQL شرطية واحدة، لا
/// تحميل-تعديل-حفظ. نفس مبرر وجود IStockOperations بالضبط، لكن على
/// aggregate مختلف — لهيك interface منفصل، لا إضافة لواجهة المخزون.
/// </summary>
public interface ISaleInvoiceOperations
{
    /// <summary>
    /// يحاول تسجيل كمية مرتجعة على سطر بيع، بشرط ذري: المجموع بعد
    /// الإضافة لا يتجاوز الكمية المباعة أصلًا. يرجّع false — بلا أي
    /// تعديل — لو الشرط انكسر.
    ///
    /// جملة واحدة:
    ///   UPDATE SaleInvoiceItems SET QuantityReturned = QuantityReturned + @qty
    ///   WHERE Id = @id AND QuantityReturned + @qty &lt;= Quantity
    ///
    /// ليش ما بكفي الفحص بالذاكرة (SaleInvoiceItem.RecordReturn):
    /// إرجاعان متزامنان لنفس السطر ممكن الاثنين يقرأوا QuantityReturned
    /// القديمة نفسها، والاثنين يمرّوا الفحص، والنتيجة إرجاع أكتر من
    /// المُباع فعليًا — بضاعة وفلوس بترجع مرتين. الشرط داخل الـWHERE
    /// بيخلي قاعدة البيانات نفسها تحسمها: الثاني بيشوف القيمة المحدَّثة
    /// وبيتأثر صفر صفوف.
    /// </summary>
    Task<bool> TryRecordReturnedQuantityAsync(Guid saleInvoiceItemId, decimal returnQuantity, CancellationToken cancellationToken);
}

/// <summary>
/// ينفّذ عملية تجارية كاملة جوّا معاملة قاعدة بيانات واحدة صريحة.
///
/// محتاجينه لأنه عملية البيع ما بتنعبّر بـSaveChangesAsync واحدة بس: فيها
/// جمل UPDATE مباشرة (خصم المخزون) متداخلة مع inserts متتبَّعة (الفاتورة،
/// الأصناف، الدفعات، حركات المخزون، سجل الدرج). لازم كل هذا يلتزم سوا أو
/// ولا شي منه — وهذا محتاج معاملة توحد الاثنين، مش المعاملة الضمنية اللي
/// SaveChangesAsync بيفتحها لحالها حول كتاباتها هي بس.
/// </summary>
public interface ITransactionalExecutor
{
    /// <summary>
    /// يعمل commit لو العملية رجعت Result ناجح؛ يعمل rollback لو رجعت
    /// فاشل أو رمى استثناء. يعني فشل تجاري (Result فاشل) بيلغي الشغل
    /// الجزئي بنفس قوة الاستثناء، بلا ما نحتاج استثناءات للتحكم بالمسار.
    /// </summary>
    Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<CancellationToken, Task<Result<TValue>>> operation,
        CancellationToken cancellationToken);
}
