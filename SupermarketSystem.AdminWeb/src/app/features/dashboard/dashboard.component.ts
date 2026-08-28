import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ReportsOperation, SalesOperation, ReviewsOperation, BackupsOperation } from '../../core/api/operations';

interface SalesSummaryPeriodDto {
  invoiceCount: number;
  totalSales: number;
  netRevenue: number;
}

interface GetSalesSummaryResponse {
  period: SalesSummaryPeriodDto;
}

interface SaleInvoiceListItemDto {
  id: string;
  invoiceNumber: string;
  statusTitle: string;
  totalAmount: number;
  createdAtUtc: string;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

interface GetPendingReviewsResponse {
  totalCount: number;
}

interface BackupItemDto {
  createdAtUtc: string;
  statusCode: number;
}

interface GetBackupsResponse {
  items: PagedResult<BackupItemDto>;
}

/**
 * أُعيد بناؤها بالكامل — كانت رسوم بيانية (أعمدة، دونات) ببيانات مختلقة
 * بالكود، بلا أي endpoint حقيقي يقابلها (لا "مبيعات يومية"، لا "توزيع
 * أقسام" موجودين بالباك إند أصلًا). استُبدلت بعناصر حقيقية 100%: ملخّص
 * مبيعات فعلي (GetSalesSummary)، تنبيهات فعلية (مخزون سالب + إعادة طلب
 * + مراجعات معلَّقة — كلها endpoints موجودة أصلًا)، وآخر الفواتير
 * الفعلية. لو احتجنا لاحقًا رسم بياني حقيقي، هذا يعني بناء endpoint
 * "مبيعات يومية" بالباك إند أول، لا اختلاق بيانات بالواجهة.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css'
})
export class DashboardComponent implements OnInit {
  readonly summary = signal<SalesSummaryPeriodDto | null>(null);
  readonly recentInvoices = signal<SaleInvoiceListItemDto[]>([]);
  readonly negativeStockCount = signal(0);
  readonly reorderNeededCount = signal(0);
  readonly pendingReviewsCount = signal(0);
  readonly loading = signal(true);

  /** null = لسه ما تحمّل / تعذّر الجلب (بلا تنبيه بهالحالة، تفاديًا لتنبيه كاذب). */
  readonly lastBackupAtUtc = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  get averageInvoice(): number {
    const s = this.summary();
    if (!s || s.invoiceCount === 0) return 0;
    return s.totalSales / s.invoiceCount;
  }

  get totalAlerts(): number {
    return this.negativeStockCount() + this.reorderNeededCount() + this.pendingReviewsCount();
  }

  /** أقدم من 24 ساعة = تنبيه. null (لسه ما توفرت بيانات) = بلا تنبيه، ما نفترض الأسوأ بلا دليل. */
  get isBackupStale(): boolean {
    const last = this.lastBackupAtUtc();
    if (!last) return false;
    const hoursSince = (Date.now() - new Date(last).getTime()) / (1000 * 60 * 60);
    return hoursSince > 24;
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);

    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - 30);

    const results = await Promise.allSettled([
      firstValueFrom(this.apiClient.get<GetSalesSummaryResponse>(ApiController.Reports, ReportsOperation.SalesSummary, undefined, {
        fromUtc: from.toISOString(), toUtc: to.toISOString()
      })),
      firstValueFrom(this.apiClient.get<PagedResult<SaleInvoiceListItemDto>>(ApiController.Sales, SalesOperation.List, undefined, {
        pageNumber: 1, pageSize: 5
      })),
      firstValueFrom(this.apiClient.get<PagedResult<unknown>>(ApiController.Reports, ReportsOperation.NegativeStock, undefined, {
        pageNumber: 1, pageSize: 1
      })),
      firstValueFrom(this.apiClient.get<PagedResult<unknown>>(ApiController.Reports, ReportsOperation.ReorderNeededProducts, undefined, {
        pageNumber: 1, pageSize: 1
      })),
      firstValueFrom(this.apiClient.get<GetPendingReviewsResponse>(ApiController.Reviews, ReviewsOperation.List)),
      // pageSize=1 كافٍ - GetBackups مرتَّبة بالأحدث أول افتراضيًا، فأول
      // عنصر هو آخر نسخة فعليًا، بلا حاجة نجيب القائمة كاملة.
      firstValueFrom(this.apiClient.get<GetBackupsResponse>(ApiController.Backups, BackupsOperation.List, undefined, {
        pageNumber: 1, pageSize: 5
      }))
    ]);

    if (results[0].status === 'fulfilled') this.summary.set(results[0].value.period);
    if (results[1].status === 'fulfilled') this.recentInvoices.set(results[1].value.items);
    if (results[2].status === 'fulfilled') this.negativeStockCount.set(results[2].value.totalCount);
    if (results[3].status === 'fulfilled') this.reorderNeededCount.set(results[3].value.totalCount);
    if (results[4].status === 'fulfilled') this.pendingReviewsCount.set(results[4].value.totalCount);
    if (results[5].status === 'fulfilled') {
      // أول نسخة ناجحة ضمن آخر 5 محاولات - لا أحدث عنصر فقط، تفاديًا
      // لتنبيه كاذب لو آخر محاولة فشلت بس قبلها نجحت وحدة.
      const lastSuccessful = results[5].value.items.items.find(b => b.statusCode === 1);
      if (lastSuccessful) {
        this.lastBackupAtUtc.set(lastSuccessful.createdAtUtc);
      }
    }

    this.loading.set(false);
  }
}
