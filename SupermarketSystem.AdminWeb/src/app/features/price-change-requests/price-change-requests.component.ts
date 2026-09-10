import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PriceChangeRequestsOperation } from '../../core/api/operations';

interface PriceChangeRequestListItemDto {
  id: string;
  productBranchId: string;
  productId: string;
  productName: string;
  branchName: string;
  previousPrice: number;
  requestedPrice: number;
  requestedByUserName: string;
  requestedAtUtc: string;
}

/** كانت مفقودة كليًا - راجع تعليق PriceChangeRequest.cs بالباك إند (سماح بثلاث مستويات لتعديل سعر بيع). */
@Component({
  selector: 'app-price-change-requests',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './price-change-requests.component.html',
  styleUrl: './price-change-requests.component.css'
})
export class PriceChangeRequestsComponent implements OnInit {
  readonly requests = signal<PriceChangeRequestListItemDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly decidingId = signal<string | null>(null);

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadRequests();
  }

  private async loadRequests(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<PriceChangeRequestListItemDto[]>(ApiController.PriceChangeRequests, PriceChangeRequestsOperation.List)
      );
      this.requests.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل طلبات تعديل السعر.');
    } finally {
      this.loading.set(false);
    }
  }

  async approve(request: PriceChangeRequestListItemDto): Promise<void> {
    this.decidingId.set(request.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.PriceChangeRequests, PriceChangeRequestsOperation.Approve, {}, { requestId: request.id })
      );
      await this.loadRequests();
    } catch {
      this.errorMessage.set('تعذّر الموافقة على الطلب.');
    } finally {
      this.decidingId.set(null);
    }
  }

  async reject(request: PriceChangeRequestListItemDto): Promise<void> {
    this.decidingId.set(request.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.PriceChangeRequests, PriceChangeRequestsOperation.Reject, {}, { requestId: request.id })
      );
      await this.loadRequests();
    } catch {
      this.errorMessage.set('تعذّر رفض الطلب.');
    } finally {
      this.decidingId.set(null);
    }
  }
}
