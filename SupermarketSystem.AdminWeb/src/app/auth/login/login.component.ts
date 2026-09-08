import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { AuthService, PublicBranchDto } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent implements OnInit {
  username = '';
  password = '';
  selectedBranchId = '';

  readonly branches = signal<PublicBranchDto[]>([]);
  readonly isSubmitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  constructor(
    private readonly authService: AuthService,
    private readonly permissionsService: PermissionsService,
    private readonly router: Router,
    private readonly route: ActivatedRoute
  ) {}

  async ngOnInit(): Promise<void> {
    // رسالة صريحة لما التوجيه لهون صار بسبب جلسة انتهت أو أوقفها الأدمن
    // (عبر authInterceptor عند 401) — لا صمت، ولا خطأ عام مربك.
    if (this.route.snapshot.queryParamMap.get('sessionExpired') === '1') {
      this.errorMessage.set('انتهت جلستك أو تم إيقافها. الرجاء تسجيل الدخول من جديد.');
    }

    try {
      const branches = await this.authService.getPublicBranches();
      this.branches.set(branches);
    } catch {
      /* فشل تحميل الفروع لا يمنع تسجيل الدخول. */
    }
  }

  async onSubmit(): Promise<void> {
    if (!this.username.trim() || !this.password) {
      this.errorMessage.set('الرجاء إدخال اسم المستخدم وكلمة السر.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const result = await this.authService.login(
      this.username.trim(),
      this.password,
      this.selectedBranchId || null
    );

    this.isSubmitting.set(false);

    if (result.success) {
      // سائق (Orders.Deliver بلا Sales.Create) يُوجَّه مباشرة لصفحته
      // الوحيدة - لا للوحة الرئيسية العادية. تحميل الصلاحيات هون (لا
      // بانتظار ShellComponent) عشان نعرف الوجهة قبل أول تنقّل.
      this.permissionsService.reset();
      await this.permissionsService.load();

      if (this.permissionsService.has('Orders.Deliver') && !this.permissionsService.has('Sales.Create')) {
        this.router.navigateByUrl('/driver');
      } else {
        this.router.navigateByUrl('/');
      }
    } else {
      this.errorMessage.set(result.message);
      this.password = '';
    }
  }
}
