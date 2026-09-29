import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { APP_INITIALIZER } from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS, DatePipeConfig } from '@angular/common';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';
import { authInterceptor } from './app/core/interceptors/auth.interceptor';
import { AuthService } from './app/core/services/auth.service';
import { BusinessTimeService } from './app/core/services/business-time.service';

// يشتغل مرة وحدة وقت إقلاع التطبيق، *قبل* ما Angular يبدأ يفعّل أي مسار
// أو يشغّل أي حارس (Guard) — هذا بالضبط اللي بيضمن استمرارية الجلسة عند
// F5: لو في refreshToken محفوظ بـsessionStorage، الجلسة تُستعاد بالكامل
// قبل ما authGuard يفحص isAuthenticated()، فما يصير توجيه خاطئ للحظة
// وحدة لصفحة الدخول ثم رجوع فوري (ومضة UI مزعجة).
function initializeAuth(authService: AuthService): () => Promise<void> {
  return () => authService.restoreSession();
}

// توقيت المحل (29/9/2026): بيتحمّل قبل أول شاشة، وكل `| date` بلوحة الإدارة بيعرض بتوقيت المحل (مش توقيت
// الجهاز) - الـgetter بيتقرأ وقت كل عرض، فتعديل الإعدادات بيبين فورًا بلا إعادة تحميل.
function initializeBusinessTime(businessTime: BusinessTimeService): () => Promise<void> {
  return () => businessTime.load();
}

function datePipeOptions(businessTime: BusinessTimeService): DatePipeConfig {
  return {
    get timezone() {
      return businessTime.timezone();
    }
  } as DatePipeConfig;
}

bootstrapApplication(AppComponent, {
  providers: [
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor])),
    {
      provide: APP_INITIALIZER,
      useFactory: initializeAuth,
      deps: [AuthService],
      multi: true
    },
    {
      provide: APP_INITIALIZER,
      useFactory: initializeBusinessTime,
      deps: [BusinessTimeService],
      multi: true
    },
    { provide: DATE_PIPE_DEFAULT_OPTIONS, useFactory: datePipeOptions, deps: [BusinessTimeService] }
  ]
}).catch(err => console.error(err));
