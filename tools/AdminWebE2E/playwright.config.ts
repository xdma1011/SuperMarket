import { defineConfig } from '@playwright/test';

/**
 * لوحة الإدارة بمتصفح حقيقي (بند 5 من "خطة اختبار آلي موسّع"). بيفترض:
 *  - الـAPI شغّال على E2E_API (افتراضي http://localhost:5000/api/v1) ومربوط بقاعدة **اختبار** (اسمها فيه test).
 *  - `ng serve` شغّال على E2E_WEB (افتراضي http://localhost:4200).
 *  - E2E_ADMIN_USER / E2E_ADMIN_PASSWORD (افتراضي admin / 123 = bootstrap-admin على قاعدة اختبار فاضية).
 * run\11-adminweb-e2e.bat بيشغّل كل هاد لحاله. المتصفح = Chrome المثبَّت (ما بننزّل متصفحات).
 */
export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  workers: 1,
  fullyParallel: false,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'report' }]],
  use: {
    baseURL: process.env.E2E_WEB ?? 'http://localhost:4200',
    channel: 'chrome',
    headless: true,
    locale: 'ar-JO',
    viewport: { width: 1440, height: 900 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure'
  }
});
