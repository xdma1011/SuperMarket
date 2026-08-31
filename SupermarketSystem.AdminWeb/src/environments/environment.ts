export const environment = {
  production: false,
  // رابط الـAPI محليًا وقت التطوير (dotnet run الافتراضي). يُستبدل ببيئة
  // production حقيقية عبر environment.prod.ts وقت البناء للنشر.
  apiBaseUrl: 'http://localhost:5200/api/v1'
};
