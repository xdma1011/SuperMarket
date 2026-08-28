import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface ModalField {
  label: string;
  placeholder: string;
  span?: 1 | 2;
}

@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './modal.component.html',
  styleUrl: './modal.component.css'
})
export class ModalComponent {
  @Input() open = false;
  @Input() title = '';
  @Input() hint = '';
  @Input() fields: ModalField[] = [];
  @Input() saveLabel = 'حفظ';

  @Output() closed = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  close(): void {
    this.closed.emit();
  }

  save(): void {
    this.saved.emit();
  }

  /** يمنع نقرة داخل بطاقة الـmodal من الانتشار للخلفية وإغلاقها معها. */
  stopPropagation(event: MouseEvent): void {
    event.stopPropagation();
  }
}
