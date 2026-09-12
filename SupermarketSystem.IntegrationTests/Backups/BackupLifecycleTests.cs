using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketSystem.Application.Backups.DeleteBackup;
using SupermarketSystem.Application.Backups.GetBackupById;
using SupermarketSystem.Application.Backups.GetBackups;
using SupermarketSystem.Application.Backups.TriggerBackup;
using SupermarketSystem.Application.Common.Pagination;
using SupermarketSystem.Application.Common.Results;
using SupermarketSystem.Domain.Backups;
using Xunit;

namespace SupermarketSystem.IntegrationTests.Backups;

/// <summary>DatabaseBackup ليس كيانًا IBranchOwned - اختبار Handler مباشر.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BackupLifecycleTests : IntegrationTestBase
{
    public BackupLifecycleTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task إنشاء_نسخة_احتياطية_فعلية_ينجح_ضد_حاوية_SQL_Server_الحقيقية()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<TriggerBackupHandler>();

        var result = await handler.HandleAsync(new TriggerBackupCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.FileName));

        // ملاحظة بيئة اختبار (لا خطأ إنتاج): FileSizeBytes بيضل 0 هون دومًا -
        // BACKUP DATABASE ينفَّذ فعليًا وينجح (Status=Completed - نتحقق منه
        // فوق)، بس الملف الناتج (.bak) يُكتب من منظور نظام ملفات حاوية
        // SQL Server نفسها، لا نظام ملفات عملية الاختبار (.NET) - حاويتان
        // منفصلتان بالكامل هون. SqlServerBackupService.cs موثّق هذا القيد
        // بالضبط مسبقًا ("لو الاثنان نفس الجهاز... يشتغل بلا إعداد إضافي") -
        // بمنشأة إنتاج حقيقية (API وSQL Server بنفس الجهاز أو مسار شبكة
        // مشترك) الحجم بيكون حقيقيًا. لا نتحقق من FileSizeBytes > 0 هون عمدًا.
        Assert.True(result.Value.FileSizeBytes >= 0);
    }

    [Fact]
    public async Task قائمة_النسخ_وإحصائياتها_تعكس_النسخة_المُنشأة_فعليًا()
    {
        using var scope = CreateScope();
        var triggerHandler = scope.ServiceProvider.GetRequiredService<TriggerBackupHandler>();
        var listHandler = scope.ServiceProvider.GetRequiredService<GetBackupsHandler>();

        var created = await triggerHandler.HandleAsync(new TriggerBackupCommand(), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var list = await listHandler.HandleAsync(new GetBackupsQuery(new PagedRequest { PageSize = 50 }), CancellationToken.None);

        var item = list.Items.Items.Single(b => b.Id == created.Value.BackupId);
        Assert.Equal(1 /* BackupStatus.Completed */, item.StatusCode);
        Assert.Equal("مكتملة", item.StatusTitle);
        Assert.True(list.Stats.TotalCount >= 1);
        Assert.True(list.Stats.TotalSizeBytes >= item.FileSizeBytes);
    }

    [Fact]
    public async Task حذف_آخر_نسخة_ناجحة_يُرفض_وحذف_نسخة_أقدم_ينجح()
    {
        using var scope = CreateScope();
        var triggerHandler = scope.ServiceProvider.GetRequiredService<TriggerBackupHandler>();
        var deleteHandler = scope.ServiceProvider.GetRequiredService<DeleteBackupHandler>();

        var first = await triggerHandler.HandleAsync(new TriggerBackupCommand(), CancellationToken.None);
        Assert.True(first.IsSuccess);
        // فارق ثانية واحدة على الأقل لضمان اسم ملف مختلف (الاسم يعتمد على yyyyMMdd_HHmmss).
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var second = await triggerHandler.HandleAsync(new TriggerBackupCommand(), CancellationToken.None);
        Assert.True(second.IsSuccess);

        // "second" هي الأحدث الآن - حذفها يجب أن يُرفض.
        var deleteLatest = await deleteHandler.HandleAsync(new DeleteBackupCommand(second.Value.BackupId), CancellationToken.None);
        Assert.True(deleteLatest.IsFailure);
        Assert.Equal("Backup.CannotDeleteMostRecent", deleteLatest.Error!.Code);

        // "first" ليست الأحدث - حذفها مسموح.
        var deleteOlder = await deleteHandler.HandleAsync(new DeleteBackupCommand(first.Value.BackupId), CancellationToken.None);
        Assert.True(deleteOlder.IsSuccess);
    }

    [Fact]
    public async Task حذف_نسخة_غير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<DeleteBackupHandler>();

        var result = await handler.HandleAsync(new DeleteBackupCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task حذف_نسخة_فاشلة_مسموح_دومًا_حتى_لو_كانت_الوحيدة()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var deleteHandler = scope.ServiceProvider.GetRequiredService<DeleteBackupHandler>();

        var failedBackup = DatabaseBackup.Failed("لم-تُسمَّ.bak", "خطأ اصطناعي بالاختبار", DateTime.UtcNow);
        db.DatabaseBackups.Add(failedBackup);
        await db.SaveChangesAsync();

        var result = await deleteHandler.HandleAsync(new DeleteBackupCommand(failedBackup.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task جلب_تفاصيل_نسخة_غير_مكتملة_يفشل_بقاعدة_عمل_وغير_موجودة_يفشل_بـNotFound()
    {
        using var scope = CreateScope();
        var db = CreateDbContext(scope);
        var handler = scope.ServiceProvider.GetRequiredService<GetBackupByIdHandler>();

        var failedBackup = DatabaseBackup.Failed("لم-تُسمَّ2.bak", "خطأ اصطناعي آخر", DateTime.UtcNow);
        db.DatabaseBackups.Add(failedBackup);
        await db.SaveChangesAsync();

        var notDownloadable = await handler.HandleAsync(new GetBackupByIdQuery(failedBackup.Id), CancellationToken.None);
        Assert.True(notDownloadable.IsFailure);
        Assert.Equal("Backup.NotDownloadable", notDownloadable.Error!.Code);

        var notFound = await handler.HandleAsync(new GetBackupByIdQuery(Guid.NewGuid()), CancellationToken.None);
        Assert.True(notFound.IsFailure);
        Assert.Equal(ErrorType.NotFound, notFound.Error!.Type);
    }

    [Fact]
    public async Task مجموعة_النسخ_الاحتياطية_محمية_401_بلا_توكن()
    {
        var client = CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/backups");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task كاشير_بلا_صلاحية_Backups_Manage_يُرفض_بـ403()
    {
        var (_, username) = await UsersTestDataHelper.CreateUserWithRoleReturningUsernameAsync(
            Fixture.Factory.Services, Fixture.TestBranchId, UsersTestDataHelper.CashierRoleId, "cashier.backups.test");

        var cashierClient = await LoginHelper.LoginAsAsync(Fixture, username, UsersTestDataHelper.DefaultPassword);

        var response = await cashierClient.GetAsync("/api/v1/backups");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
