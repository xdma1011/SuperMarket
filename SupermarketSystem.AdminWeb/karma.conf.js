// راجع https://karma-runner.github.io/6.4/config/configuration-file.html
// المسار الافتراضي للكونتينر السحابي بس لو موجود فعلًا - على ويندوز (جهاز صاحب المشروع) بنخلّي karma-chrome-launcher
// يلاقي Chrome المثبَّت لحاله (كان run\6-tests-web.bat بيفشل "Can not find the binary \opt\pw-browsers\chromium").
if (!process.env.CHROME_BIN && require('fs').existsSync('/opt/pw-browsers/chromium')) {
  process.env.CHROME_BIN = '/opt/pw-browsers/chromium';
}

module.exports = function (config) {
  config.set({
    basePath: '',
    frameworks: ['jasmine', '@angular-devkit/build-angular'],
    plugins: [
      require('karma-jasmine'),
      require('karma-chrome-launcher'),
      require('karma-jasmine-html-reporter'),
      require('karma-coverage'),
      require('@angular-devkit/build-angular/plugins/karma'),
    ],
    client: {
      jasmine: {},
      clearContext: false,
    },
    jasmineHtmlReporter: {
      suppressAll: true,
    },
    coverageReporter: {
      dir: require('path').join(__dirname, './coverage/supermarket-admin-web'),
      subdir: '.',
      reporters: [{ type: 'html' }, { type: 'text-summary' }],
    },
    reporters: ['progress', 'kjhtml'],
    port: 9876,
    colors: true,
    logLevel: config.LOG_INFO,
    autoWatch: true,
    // بلا Chrome حقيقي بهاي البيئة (كونتينر سحابي بلا واجهة) — Chromium
    // المثبَّت مسبقًا (راجع تعليمات البيئة) بوضع headless، بلا sandbox
    // (تشغيل كـroot جوّا كونتينر).
    customLaunchers: {
      ChromeHeadlessCI: {
        base: 'ChromeHeadless',
        flags: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
      },
    },
    browsers: ['ChromeHeadlessCI'],
    singleRun: true,
    restartOnFileChange: false,
  });
};
