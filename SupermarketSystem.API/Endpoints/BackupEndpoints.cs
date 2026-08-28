using SupermarketSystem.API.Common;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Backups.DeleteBackup;
using SupermarketSystem.Application.Backups.GetBackupById;
using SupermarketSystem.Application.Backups.GetBackups;
using SupermarketSystem.Application.Backups.TriggerBackup;
using SupermarketSystem.Application.Common.Pagination;

namespace SupermarketSystem.API.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/backups").WithTags("Backups").RequirePermission(PermissionCodes.BackupsManage);

        group.MapPost("/", async (
            TriggerBackupHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new TriggerBackupCommand(), cancellationToken);
            return result.ToHttpResult(response => Results.Created($"/api/v1/backups/{response.BackupId}", response));
        })
        .WithName("TriggerBackup")
        .WithSummary("ينشئ نسخة احتياطية فورًا (بالإضافة للتشغيل التلقائي اليومي)، وينظّف القديم حسب حد الاحتفاظ.")
        .Produces<TriggerBackupResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ينشئ نسخة جديدة *وينزّلها مباشرة بنفس الطلب* — لا خطوتين منفصلتين
        // (إنشاء ثم تنزيل لاحقًا). يعيد استخدام نفس منطق TriggerBackupHandler
        // بلا أي تكرار: يستدعيه أول، وبعدين يجيب مسار الملف الناتج ويبثّه.
        //
        // تنبيه مهم: الطلب بيضل مفتوح لحد ما BACKUP DATABASE يخلص فعليًا
        // (نفس مدة /backups العادية — العملية أصلًا متزامنة، بلا فرق زمني
        // إضافي بسبب الدمج). لقاعدة بيانات كبيرة ممكن ياخد دقائق — الجهاز
        // الطالب لازم يتحمّل انتظار مماثل، مش شرط جديد إضافي.
        group.MapPost("/download", async (
            TriggerBackupHandler triggerHandler,
            GetBackupByIdHandler getByIdHandler,
            CancellationToken cancellationToken) =>
        {
            var triggerResult = await triggerHandler.HandleAsync(new TriggerBackupCommand(), cancellationToken);
            if (triggerResult.IsFailure)
            {
                return triggerResult.ToHttpResult();
            }

            var detailsResult = await getByIdHandler.HandleAsync(
                new GetBackupByIdQuery(triggerResult.Value.BackupId), cancellationToken);
            if (detailsResult.IsFailure)
            {
                return detailsResult.ToHttpResult();
            }

            var details = detailsResult.Value;
            if (!File.Exists(details.FilePath))
            {
                return Results.Problem(
                    title: "Backup.FileMissing",
                    detail: "النسخة اتسجّلت بنجاح لكن الملف غير موجود على القرص فعليًا.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.File(details.FilePath, "application/octet-stream", details.FileName);
        })
        .WithName("TriggerAndDownloadBackup")
        .WithSummary("ينشئ نسخة احتياطية جديدة ويرسلها مباشرة كملف بنفس الطلب — لا خطوتين منفصلتين.")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            int? pageNumber, int? pageSize, string? search, string? sortBy, string? sortDirection,
            GetBackupsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paging = PagingBinder.Build(pageNumber, pageSize, search, sortBy, sortDirection);
            var result = await handler.HandleAsync(new GetBackupsQuery(paging), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("GetBackups")
        .Produces<GetBackupsResponse>(StatusCodes.Status200OK);

        // قراءة الملف من القرص وبثّه — هذا تحديدًا سبب بقاء المنطق هون
        // بالـAPI layer، لا Application: "بث ملف كاستجابة HTTP" تفصيل نقل
        // بحت، بلا أي قرار بزنس فيه.
        group.MapGet("/{backupId:guid}/download", async (
            Guid backupId,
            GetBackupByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new GetBackupByIdQuery(backupId), cancellationToken);

            if (result.IsFailure)
            {
                return result.ToHttpResult();
            }

            var details = result.Value;
            if (!File.Exists(details.FilePath))
            {
                return Results.Problem(
                    title: "Backup.FileMissing",
                    detail: "السجل موجود بقاعدة البيانات لكن الملف الفعلي غير موجود على القرص — ممكن يكون انحذف يدويًا أو انتقل.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.File(details.FilePath, "application/octet-stream", details.FileName);
        })
        .WithName("DownloadBackup")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{backupId:guid}", async (
            Guid backupId,
            DeleteBackupHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new DeleteBackupCommand(backupId), cancellationToken);
            return result.ToHttpResult();
        })
        .WithName("DeleteBackup")
        .WithSummary("يحذف نسخة احتياطية محددة. آخر نسخة ناجحة محمية من الحذف دائمًا.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
