export const environment = {
  production: false,
  // رابط الـAPI محليًا وقت التطوير (dotnet run الافتراضي). يُستبدل ببيئة
  // production حقيقية عبر environment.prod.ts وقت البناء للنشر.
  apiBaseUrl: 'https://localhost:7001/api/v1'
};
