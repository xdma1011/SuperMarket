import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrowserMultiFormatReader } from '@zxing/browser';
import { BarcodeScannerComponent } from './barcode-scanner.component';

/**
 * @zxing/browser بيلمس كاميرا حقيقية (getUserMedia) — غير متاحة أصلًا
 * بمتصفح Headless بلا جهاز فعلي. الاختبارات هون بتموك الطبقة الساكنة
 * (listVideoInputDevices) وميثود القراءة (decodeFromVideoDevice) بالكامل،
 * بلا أي وصول فعلي للكاميرا - تغطي منطق اختيار الكاميرا الخلفية، معالجة
 * نتيجة المسح، ومعالجة الأخطاء (رفض الإذن/عدم وجود كاميرا)، وclose/ngOnDestroy.
 */
describe('BarcodeScannerComponent', () => {
  let fixture: ComponentFixture<BarcodeScannerComponent>;
  let component: BarcodeScannerComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BarcodeScannerComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(BarcodeScannerComponent);
    component = fixture.componentInstance;
    component.videoElementRef = { nativeElement: document.createElement('video') } as never;
  });

  it('يُنشأ المكوّن بنجاح، ببدء تشغيل=true قبل التهيئة', () => {
    expect(component).toBeTruthy();
    expect(component.starting()).toBeTrue();
    expect(component.cameraError()).toBeNull();
  });

  describe('ngAfterViewInit', () => {
    it('يختار الكاميرا الخلفية (label يحتوي back/rear/environment) لا أول كاميرا', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.resolveTo([
        { deviceId: 'front-1', label: 'Front Camera', kind: 'videoinput', groupId: '' } as MediaDeviceInfo,
        { deviceId: 'back-1', label: 'Back Camera (environment)', kind: 'videoinput', groupId: '' } as MediaDeviceInfo
      ]);
      const decodeSpy = spyOn(BrowserMultiFormatReader.prototype, 'decodeFromVideoDevice').and.resolveTo({
        stop: () => {}
      } as never);

      await component.ngAfterViewInit();

      expect(decodeSpy).toHaveBeenCalledWith('back-1', jasmine.anything(), jasmine.any(Function));
      expect(component.starting()).toBeFalse();
      expect(component.cameraError()).toBeNull();
    });

    it('يستخدم أول كاميرا متاحة لو ما في وحدة تحمل تسمية كاميرا خلفية', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.resolveTo([
        { deviceId: 'only-1', label: 'Camera', kind: 'videoinput', groupId: '' } as MediaDeviceInfo
      ]);
      const decodeSpy = spyOn(BrowserMultiFormatReader.prototype, 'decodeFromVideoDevice').and.resolveTo({
        stop: () => {}
      } as never);

      await component.ngAfterViewInit();

      expect(decodeSpy).toHaveBeenCalledWith('only-1', jasmine.anything(), jasmine.any(Function));
    });

    it('يعرض رسالة عربية واضحة لو ما في أي كاميرا على الجهاز', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.resolveTo([]);

      await component.ngAfterViewInit();

      expect(component.cameraError()).toBe('لا توجد كاميرا متاحة على هذا الجهاز.');
      expect(component.starting()).toBeFalse();
    });

    it('يعرض رسالة عربية واضحة لو فشل الوصول للكاميرا (رفض إذن مثلًا)', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.rejectWith(new Error('NotAllowedError'));

      await component.ngAfterViewInit();

      expect(component.cameraError()).toBe('تعذّر الوصول للكاميرا — تأكد من إعطاء الإذن المطلوب بالمتصفح.');
      expect(component.starting()).toBeFalse();
    });

    it('يبعث حدث scanned بالنص المقروء عند نجاح المسح', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.resolveTo([
        { deviceId: 'back-1', label: 'Back Camera', kind: 'videoinput', groupId: '' } as MediaDeviceInfo
      ]);

      let capturedCallback: ((result: { getText(): string } | undefined) => void) | undefined;
      spyOn(BrowserMultiFormatReader.prototype, 'decodeFromVideoDevice').and.callFake(
        (async (_id: string, _el: unknown, cb: (result: { getText(): string } | undefined) => void) => {
          capturedCallback = cb;
          return { stop: () => {} };
        }) as never
      );

      const emitted: string[] = [];
      component.scanned.subscribe(text => emitted.push(text));

      await component.ngAfterViewInit();
      capturedCallback?.({ getText: () => '6291041500213' });

      expect(emitted).toEqual(['6291041500213']);
    });
  });

  describe('close', () => {
    it('يبعث حدث closed', () => {
      let emitted = false;
      component.closed.subscribe(() => (emitted = true));

      component.close();

      expect(emitted).toBeTrue();
    });
  });

  describe('ngOnDestroy', () => {
    it('يوقف controls لو موجودة بلا رمي أي خطأ', async () => {
      spyOn(BrowserMultiFormatReader, 'listVideoInputDevices').and.resolveTo([
        { deviceId: 'back-1', label: 'Back Camera', kind: 'videoinput', groupId: '' } as MediaDeviceInfo
      ]);
      const stopSpy = jasmine.createSpy('stop');
      spyOn(BrowserMultiFormatReader.prototype, 'decodeFromVideoDevice').and.resolveTo({ stop: stopSpy } as never);

      await component.ngAfterViewInit();
      component.ngOnDestroy();

      expect(stopSpy).toHaveBeenCalled();
    });

    it('لا يرمي خطأ لو ngOnDestroy استُدعي بلا أي تهيئة سابقة للماسح', () => {
      expect(() => component.ngOnDestroy()).not.toThrow();
    });
  });
});
