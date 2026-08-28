import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { APP_INITIALIZER } from '@angular/core';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';
import { authInterceptor } from './app/core/interceptors/auth.interceptor';
import { AuthService } from './app/core/services/auth.service';

// يشتغل مرة وحدة وقت إقلاع التطبيق، *قبل* ما Angular يبدأ يفعّل أي مسار
// أو يشغّل أي حارس (Guard) — هذا بالضبط اللي بيضمن استمرارية الجلسة عند
// F5: لو في refreshToken محفوظ بـsessionStorage، الجلسة تُستعاد بالكامل
// قبل ما authGuard يفحص isAuthenticated()، فما يصير توجيه خاطئ للحظة
// وحدة لصفحة الدخول ثم رجوع فوري (ومضة UI مزعجة).
function initializeAuth(authService: AuthService): () => Promise<void> {
  return () => authService.restoreSession();
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
    }
  ]
}).catch(err => console.error(err));
