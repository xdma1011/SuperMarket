using Microsoft.AspNetCore.Identity;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Domain.Identity;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// يستخدم PasswordHasher&lt;T&gt; من ASP.NET Core Identity — خوارزمية
/// PBKDF2 بملح عشوائي لكل كلمة سر، مراجَعة أمنيًا ومصانة من مايكروسوفت.
///
/// المهم إننا نستعمل *مُجزّئ* Identity فقط، لا نظام Identity كامل: ما في
/// IdentityUser ولا IdentityDbContext ولا جداول Identity الجاهزة. كيان
/// User اللي بنيناه بيضل هو مصدر الحقيقة الوحيد بلا أي تغيير — أخذنا
/// القطعة الوحيدة اللي فعلًا خطر نكتبها بأنفسنا (تجزئة كلمات السر)، وتركنا
/// الباقي.
///
/// كتابة تجزئة كلمة سر يدويًا (SHA + ملح مثلًا) من أكثر الأخطاء الأمنية
/// شيوعًا وأسهلها وقوعًا — SHA سريعة عمدًا، وهذا بالضبط اللي بيخلي كسرها
/// بالقوة الغاشمة رخيصًا. PBKDF2 بطيئة عمدًا، وهذا هو المطلوب هون.
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    // النسخة المستخدمة من PasswordHasher ما بتحتاج كائن المستخدم فعليًا
    // (بس بالتوقيع)، فنمرّر واحدًا ثابتًا بدل ما نطلب من المستدعي يمرّر
    // مستخدمًا لا علاقة له بالعملية.
    private static readonly User HashingContext = new("hashing-context", "hashing-context", "hashing@context.local");

    public string Hash(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword))
        {
            throw new ArgumentException("كلمة السر مطلوبة.", nameof(plainPassword));
        }

        return _hasher.HashPassword(HashingContext, plainPassword);
    }

    public PasswordVerificationOutcome Verify(string plainPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(storedHash))
        {
            return PasswordVerificationOutcome.Failed;
        }

        // VerifyHashedPassword نفسها محمية ضد هجمات التوقيت (تقارن بزمن
        // ثابت) — نقطة أخرى ما كنا لنضبطها بسهولة لو كتبناها بأنفسنا.
        // مضمون غير null/فاضي هون - الفحص أعلاه (IsNullOrWhiteSpace) بيرجع
        // مبكرًا قبل ما نوصل لهون.
        var cleanHash = storedHash!.Trim();

        try
        {
            var result = _hasher.VerifyHashedPassword(HashingContext, cleanHash, plainPassword);

            return result switch
            {
                PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
                _ => PasswordVerificationOutcome.Failed
            };
        }
        catch (FormatException)
        {
            // PasswordHash مخزَّن مش Base64 صحيح (بيانات تالفة - تعديل يدوي
            // غلط بـSSMS، استيراد قديم، إلخ) - VerifyHashedPassword نفسها ما
            // بتلقط هالحالة وبترمي استثناء يطيح كل طلب الدخول بـ500. المبدأ
            // الحاكم لهالـclass (رسالة فشل واحدة لكل الأسباب، راجع تعليق
            // LoginHandler) بينطبق هون بالضبط: بيانات مشوَّهة = فشل تحقق
            // عادي، مش كرش.
            return PasswordVerificationOutcome.Failed;
        }
    }
}
