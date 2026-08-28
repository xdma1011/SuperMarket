import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-placeholder',
  standalone: true,
  template: `
    <div class="page-head">
      <div class="head-text">
        <span class="crumb">قريبًا</span>
        <h1>{{ title }}</h1>
        <span class="subtitle">هذه الشاشة بانتظار البناء الكامل — التنقل والهيكل العام جاهزان.</span>
      </div>
    </div>
    <div class="empty-panel">
      <span>لا يوجد محتوى بعد بهذا القسم.</span>
    </div>
  `,
  styles: [`
    .empty-panel {
      background: var(--surface); border: 1px dashed var(--line); border-radius: 16px;
      padding: 60px 20px; display: flex; align-items: center; justify-content: center;
      color: var(--ink-2); font-size: 13px;
    }
  `]
})
export class PlaceholderComponent {
  @Input() title = '';
}
