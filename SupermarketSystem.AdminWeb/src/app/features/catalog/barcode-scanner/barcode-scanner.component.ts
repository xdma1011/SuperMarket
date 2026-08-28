import { Component, ElementRef, EventEmitter, OnDestroy, Output, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';

/**
 * "امسح أول" — يفتح كاميرا الموبايل مباشرة، يمسح باركود حي. المكوّن
 * الأب (CatalogComponent) بيقرر شو يعمل بالنتيجة (يفتح منتج موجود، أو
 * نموذج جديد بالباركود معبّى مسبقًا) عبر استدعاء GetProductByBarcode.
 *
 * @zxing/browser مُختار عمدًا — دعم موثوق لـChrome على أندرويد (بيئة
 * المستخدم الفعلية)، بلا الحاجة لخادم/معالجة صور إضافية.
 */
@Component({
  selector: 'app-barcode-scanner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './barcode-scanner.component.html',
  styleUrl: './barcode-scanner.component.css'
})
export class BarcodeScannerComponent implements OnDestroy {
  @ViewChild('videoElement') videoElementRef!: ElementRef<HTMLVideoElement>;
  @Output() scanned = new EventEmitter<string>();
  @Output() closed = new EventEmitter<void>();

  readonly cameraError = signal<string | null>(null);
  readonly starting = signal(true);

  private controls: IScannerControls | null = null;
  private reader: BrowserMultiFormatReader | null = null;

  async ngAfterViewInit(): Promise<void> {
    this.reader = new BrowserMultiFormatReader();

    try {
      const devices = await BrowserMultiFormatReader.listVideoInputDevices();
      const backCamera = devices.find(d => /back|rear|environment/i.test(d.label)) ?? devices[0];

      if (!backCamera) {
        this.cameraError.set('لا توجد كاميرا متاحة على هذا الجهاز.');
        this.starting.set(false);
        return;
      }

      this.controls = await this.reader.decodeFromVideoDevice(
        backCamera.deviceId,
        this.videoElementRef.nativeElement,
        (result) => {
          if (result) {
            this.scanned.emit(result.getText());
          }
        }
      );

      this.starting.set(false);
    } catch {
      this.cameraError.set('تعذّر الوصول للكاميرا — تأكد من إعطاء الإذن المطلوب بالمتصفح.');
      this.starting.set(false);
    }
  }

  close(): void {
    this.closed.emit();
  }

  ngOnDestroy(): void {
    this.controls?.stop();
  }
}
