import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { UnitsOfMeasureOperation } from '../../core/api/operations';

interface UnitOfMeasureDto {
  id: string;
  name: string;
  isActive: boolean;
}

/** كانت مفقودة بالكامل - وحدة كل منتج كانت نص حر بلا مرجع موحَّد، سبب تكرار مسميات مختلفة لنفس الوحدة. */
@Component({
  selector: 'app-units-of-measure',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './units-of-measure.component.html',
  styleUrl: './units-of-measure.component.css'
})
export class UnitsOfMeasureComponent implements OnInit {
  readonly units = signal<UnitOfMeasureDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly togglingId = signal<string | null>(null);

  readonly formOpen = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);
  newUnitName = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadUnits();
  }

  private async loadUnits(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.apiClient.get<UnitOfMeasureDto[]>(ApiController.UnitsOfMeasure, UnitsOfMeasureOperation.List)
      );
      this.units.set(result);
    } catch {
      this.errorMessage.set('تعذّر تحميل وحدات القياس.');
    } finally {
      this.loading.set(false);
    }
  }

  openCreateForm(): void {
    this.formOpen.set(true);
    this.formError.set(null);
    this.newUnitName = '';
  }

  closeForm(): void {
    this.formOpen.set(false);
  }

  async submit(): Promise<void> {
    if (!this.newUnitName.trim()) {
      this.formError.set('اسم وحدة القياس مطلوب.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.UnitsOfMeasure, UnitsOfMeasureOperation.Create, {
          name: this.newUnitName.trim()
        })
      );

      this.formOpen.set(false);
      await this.loadUnits();
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.formError.set(message ?? 'تعذّر إنشاء وحدة القياس.');
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleActive(unit: UnitOfMeasureDto): Promise<void> {
    this.togglingId.set(unit.id);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.UnitsOfMeasure, UnitsOfMeasureOperation.SetActive, {
          isActive: !unit.isActive
        }, { unitOfMeasureId: unit.id })
      );
      await this.loadUnits();
    } catch {
      this.errorMessage.set('تعذّر تغيير حالة وحدة القياس.');
    } finally {
      this.togglingId.set(null);
    }
  }
}
