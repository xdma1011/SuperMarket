namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// توكن باركود QR ثابت لهوية الزبون - يُعرض بتطبيق الزبائن كـQR، والكاشير
/// يمسحه ليعرف "مين هذا الزبون" بلا ما يقدر الزبون يزوّر رقم هاتف بالكتابة
/// اليدوية (راجع نقاش صاحب المشروع - منع التلاعب برقم الهاتف بالكاشير).
///
/// بلا انتهاء صلاحية عمدًا (خلافًا لتوكن OTP/الجلسة) - هوية دائمة، ليست
/// جلسة. الحماية الوحيدة هي التوقيع (HMAC) - أي تعديل بالمحتوى يُكتشف
/// فورًا. لا حالة مخزَّنة بقاعدة البيانات - التوكن محسوب حيًّا من
/// customerId فقط، فما فيه شيء يحتاج Migration.
/// </summary>
public interface IQrTokenService
{
    string GenerateCustomerQrToken(Guid customerId);

    /// <summary>يرجّع customerId لو التوقيع صحيح، وإلا null.</summary>
    Guid? ValidateCustomerQrToken(string token);
}
