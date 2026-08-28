// سكربت لمرة واحدة — يطبع تجزئة كلمة سر جاهزة للصق مباشرة بعمود
// Users.PasswordHash. يستخدم PasswordHasher<T> نفسه من ASP.NET Core
// Identity (نفس AspNetPasswordHasher بالمشروع بالضبط) — الملح مُدمَج
// تلقائيًا داخل التجزئة الناتجة، بلا أي عمود أو خطوة إضافية.

using Microsoft.AspNetCore.Identity;

Console.Write("اكتب كلمة السر: ");
var password = Console.ReadLine();

if (string.IsNullOrWhiteSpace(password))
{
    Console.WriteLine("كلمة سر فاضية — أعد المحاولة.");
    return;
}

var hasher = new PasswordHasher<object>();
var hash = hasher.HashPassword(new object(), password);

Console.WriteLine();
Console.WriteLine("انسخ القيمة التالية بالكامل لعمود PasswordHash:");
Console.WriteLine(hash);
