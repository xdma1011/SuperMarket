namespace SupermarketSystem.Application.Common.Interfaces;

public enum PasswordVerificationOutcome
{
    Failed = 0,
    Success = 1,

    /// <summary>
    /// كلمة السر صحيحة، لكن البصمة المخزَّنة مبنية بإعدادات قديمة (تكرارات
    /// أقل مثلًا). هاي فرصة نعيد تجزئتها بالإعدادات الحالية بصمت — لحظة
    /// التحقق هي اللحظة الوحيدة اللي فيها كلمة السر الأصلية متاحة، فما في
    /// طريقة تانية نرقّي البصمة إلا هون.
    /// </summary>
    SuccessRehashNeeded = 2
}

/// <summary>
/// تجزئة كلمة السر معزولة خلف واجهة — لا لأن التصميم يحب التجريد، بل لأن
/// الخوارزمية نفسها بتتغيّر مع الوقت (توصيات الأمان بتتشدّد كل كم سنة).
/// عزلها هون بيخلي تغييرها لاحقًا يلمس ملفًا واحدًا لا كل مسار مصادقة.
///
/// ملاحظة مقصودة: ما في ولا ميثود بترجّع كلمة السر الأصلية. التجزئة
/// باتجاه واحد بطبيعتها، والواجهة نفسها بتعكس هذا — استعادة كلمة سر منسية
/// معناها *تعيينها من جديد*، لا استرجاعها.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string plainPassword);

    PasswordVerificationOutcome Verify(string plainPassword, string storedHash);
}
