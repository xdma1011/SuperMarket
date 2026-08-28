import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

/**
 * ترقيم صفحات حقيقي — بيرسل pageNumber/pageSize فعليًا لكل استدعاء API،
 * لا "يظهر" ترقيم صفحات على بيانات محمَّلة بالكامل بالذاكرة. لهذا يحتاج
 * المكوّن الأب يستدعي API من جديد عند كل تغيير صفحة (عبر changed@).
 */
@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pagination.component.html',
  styleUrl: './pagination.component.css'
})
export class PaginationComponent {
  @Input() pageNumber = 1;
  @Input() pageSize = 20;
  @Input() totalCount = 0;

  @Output() changed = new EventEmitter<{ pageNumber: number; pageSize: number }>();

  readonly pageSizeOptions = [10, 20, 50, 100];

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  get fromRecord(): number {
    return this.totalCount === 0 ? 0 : (this.pageNumber - 1) * this.pageSize + 1;
  }

  get toRecord(): number {
    return Math.min(this.pageNumber * this.pageSize, this.totalCount);
  }

  get canGoPrev(): boolean {
    return this.pageNumber > 1;
  }

  get canGoNext(): boolean {
    return this.pageNumber < this.totalPages;
  }

  goTo(page: number): void {
    if (page < 1 || page > this.totalPages || page === this.pageNumber) return;
    this.changed.emit({ pageNumber: page, pageSize: this.pageSize });
  }

  onPageSizeChange(newSize: number): void {
    this.changed.emit({ pageNumber: 1, pageSize: Number(newSize) });
  }
}
