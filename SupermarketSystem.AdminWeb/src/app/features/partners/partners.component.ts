import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { PartnersOperation, UsersOperation } from '../../core/api/operations';
import { AuthService, PublicBranchDto } from '../../core/services/auth.service';
import { code39Svg } from '../../shared/utils/code39';

export type PartnersTab = 'partners' | 'statements' | 'withdrawals' | 'owner';

export interface PartnerDto {
  id: string;
  branchId: string;
  fullName: string;
  typeCode: number;
  typeTitle: string;
  userId: string | null;
  username: string | null;
  speculativeProfitPercent: number | null;
  isActive: boolean;
  notes: string | null;
  capitalBalance: number;
  sharesTotal: number;
  withdrawalsTotal: number;
  atCostDeductedTotal: number;
  currentBalance: number;
  /** رقم تلغرام الشريك لكود التحقق بالكاشير (29/9/2026). */
  telegramPhone?: string | null;
  /** الرقم مربوط فعليًا ببوت تلغرام (الشريك فتح البوت وشارك رقمه) - بدونه الكود ما بيوصل. */
  telegramLinked?: boolean;
  hasCashierBarcode?: boolean;
  cashierBarcodeIssuedAtUtc?: string | null;
}

/** كرت الباركود بعد الإصدار - الأرقام بتبين هون مرة وحدة بس (السيرفر بيحفظ hash). */
export interface IssuedBarcodeCard {
  partnerName: string;
  barcode: string;
  svg: SafeHtml;
  rawSvg: string;
}

export interface PartnerStatementSummaryDto {
  id: string;
  year: number;
  month: number;
  netProfit: number;
  unallocatedAmount: number;
  generatedAtUtc: string;
  isAutomatic: boolean;
  partnerCount: number;
}

export interface PartnerStatementDto extends PartnerStatementSummaryDto {
  branchName: string;
  lines: {
    partnerId: string;
    partnerName: string;
    typeCode: number;
    typeTitle: string;
    capitalBalance: number | null;
    sharePercent: number;
    shareAmount: number;
  }[];
}

export interface PartnerWithdrawalDto {
  id: string;
  partnerId: string;
  partnerName: string;
  amount: number;
  sourceCode: number;
  sourceTitle: string;
  occurredAtUtc: string;
  notes: string | null;
  recordedByName: string;
  paidByName: string | null;
  recordedAtCashier: boolean;
}

export interface PartnerLedgerDto {
  partnerId: string;
  partnerName: string;
  currentBalance: number;
  entries: { occurredAtUtc: string; kindCode: string; kindTitle: string; description: string; amount: number; runningBalance: number }[];
}

export interface OwnerReceivableDto {
  branchId: string;
  branchName: string;
  ownerUserId: string;
  ownerName: string;
  balance: number;
  entries: { id: string; typeCode: number; typeTitle: string; amount: number; occurredAtUtc: string; sourceTitle: string | null; notes: string | null }[];
}

interface UserOption {
  userId: string;
  fullName: string;
  username: string;
}

/**
 * وحدة الشركاء (28/9/2026، قرارات صاحب المشروع بـCLAUDE.md): شركاء لكل فرع (رأس مال أو مضارب)، كشف شهري بينزل
 * تلقائيًا ببداية الشهر (وبينعاد إصداره يدويًا)، سحوبات من الصندوق أو "من جيبي"، و"مستحق لصاحب المحل".
 * رأس مال الشريك بينسجّل من صفحة المالية (حركة رأس مال مع اختيار الشريك).
 */
@Component({
  selector: 'app-partners',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './partners.component.html',
  styleUrl: './partners.component.css'
})
export class PartnersComponent implements OnInit {
  private readonly apiClient = inject(ApiClient);
  private readonly auth = inject(AuthService);
  /** اختياري: "?tab=withdrawals" من كبسة تنبيه سحب شريك. */
  private readonly route = inject(ActivatedRoute, { optional: true });

  readonly tab = signal<PartnersTab>('partners');
  readonly branches = signal<PublicBranchDto[]>([]);
  branchId = '';

  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  readonly loading = signal(false);

  // الشركاء
  readonly partners = signal<PartnerDto[]>([]);
  readonly activePartners = computed(() => this.partners().filter(p => p.isActive));
  readonly users = signal<UserOption[]>([]);
  readonly partnerFormOpen = signal(false);
  editingPartnerId: string | null = null;
  formName = '';
  formType: 'Capital' | 'Speculative' = 'Capital';
  formUserId = '';
  formPercent: number | null = null;
  formNotes = '';
  formTelegramPhone = '';
  readonly issuedCard = signal<IssuedBarcodeCard | null>(null);
  private readonly sanitizer = inject(DomSanitizer);
  readonly ledger = signal<PartnerLedgerDto | null>(null);

  // الكشوف
  readonly statements = signal<PartnerStatementSummaryDto[]>([]);
  readonly selectedStatement = signal<PartnerStatementDto | null>(null);
  statementMonth = PartnersComponent.previousMonthValue();

  // السحوبات
  readonly withdrawals = signal<PartnerWithdrawalDto[]>([]);
  withdrawalPartnerId = '';
  withdrawalAmount: number | null = null;
  withdrawalSource: 'Drawer' | 'OwnerPocket' = 'Drawer';
  withdrawalNotes = '';

  // المستحق لصاحب المحل
  readonly receivables = signal<OwnerReceivableDto[]>([]);
  repayAmount: Record<string, number | null> = {};
  repaySource: Partial<Record<string, 'Drawer' | 'Outside'>> = {};

  readonly submitting = signal(false);

  static previousMonthValue(): string {
    const now = new Date();
    const d = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  }

  async ngOnInit(): Promise<void> {
    const tab = this.route?.snapshot.queryParamMap.get('tab') as PartnersTab | null;
    if (tab && ['partners', 'statements', 'withdrawals', 'owner'].includes(tab)) this.tab.set(tab);
    try {
      const branches = await this.auth.getPublicBranches();
      this.branches.set(branches);
      this.branchId = this.auth.defaultBranchId(branches);
    } catch {
      this.branchId = this.auth.currentBranchId() ?? '';
    }
    await this.reloadAll();
  }

  setTab(tab: PartnersTab): void {
    this.tab.set(tab);
    this.clearMessages();
  }

  async onBranchChange(): Promise<void> {
    this.selectedStatement.set(null);
    this.ledger.set(null);
    await this.reloadAll();
  }

  async reloadAll(): Promise<void> {
    if (!this.branchId) return;
    this.loading.set(true);
    try {
      const [partners, statements, withdrawals, receivables] = await Promise.all([
        this.get<PartnerDto[]>(PartnersOperation.List),
        this.get<PartnerStatementSummaryDto[]>(PartnersOperation.Statements),
        this.get<PartnerWithdrawalDto[]>(PartnersOperation.Withdrawals),
        this.get<OwnerReceivableDto[]>(PartnersOperation.OwnerReceivables)
      ]);
      this.partners.set(partners);
      this.statements.set(statements);
      this.withdrawals.set(withdrawals);
      this.receivables.set(receivables);
      if (!this.withdrawalPartnerId || !partners.some(p => p.id === this.withdrawalPartnerId && p.isActive)) {
        this.withdrawalPartnerId = partners.find(p => p.isActive)?.id ?? '';
      }
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تحميل بيانات الشركاء.');
    } finally {
      this.loading.set(false);
    }
  }

  private get<T>(operation: PartnersOperation, routeParams?: Record<string, string>): Promise<T> {
    return firstValueFrom(this.apiClient.get<T>(ApiController.Partners, operation, routeParams, { branchId: this.branchId }));
  }

  // ===== الشركاء =====

  async openPartnerForm(partner?: PartnerDto): Promise<void> {
    this.clearMessages();
    this.editingPartnerId = partner?.id ?? null;
    this.formName = partner?.fullName ?? '';
    this.formType = partner?.typeCode === 2 ? 'Speculative' : 'Capital';
    this.formUserId = partner?.userId ?? '';
    this.formPercent = partner?.speculativeProfitPercent ?? null;
    this.formNotes = partner?.notes ?? '';
    this.formTelegramPhone = partner?.telegramPhone ?? '';
    this.partnerFormOpen.set(true);
    if (this.users().length === 0) {
      try {
        const result = await firstValueFrom(
          this.apiClient.get<{ items: UserOption[] } | UserOption[]>(ApiController.Users, UsersOperation.List, undefined, { pageSize: 500 })
        );
        this.users.set(Array.isArray(result) ? result : result.items);
      } catch {
        this.users.set([]);
      }
    }
  }

  closePartnerForm(): void {
    this.partnerFormOpen.set(false);
  }

  async savePartner(): Promise<void> {
    if (!this.formName.trim()) {
      this.errorMessage.set('اكتب اسم الشريك.');
      return;
    }
    if (this.formType === 'Speculative' && (!this.formPercent || this.formPercent <= 0 || this.formPercent > 100)) {
      this.errorMessage.set('نسبة المضارب لازم تكون بين 0 و100.');
      return;
    }

    this.submitting.set(true);
    this.clearMessages();
    const body = {
      fullName: this.formName.trim(),
      userId: this.formUserId || null,
      speculativeProfitPercent: this.formType === 'Speculative' ? this.formPercent : null,
      notes: this.formNotes.trim() || null
    };
    const telegramPhone = this.formTelegramPhone.trim() || null;
    const previousTelegramPhone = this.partners().find(p => p.id === this.editingPartnerId)?.telegramPhone ?? null;
    try {
      let partnerId = this.editingPartnerId;
      if (partnerId) {
        await firstValueFrom(this.apiClient.put(ApiController.Partners, PartnersOperation.Update, body, { id: partnerId }));
      } else {
        const created = await firstValueFrom(
          this.apiClient.post<{ partnerId: string }>(ApiController.Partners, PartnersOperation.Create, { ...body, branchId: this.branchId, type: this.formType })
        );
        partnerId = created.partnerId;
      }
      if (partnerId && telegramPhone !== previousTelegramPhone) {
        await firstValueFrom(this.apiClient.put(ApiController.Partners, PartnersOperation.TelegramPhone, { telegramPhone }, { id: partnerId }));
      }
      this.partnerFormOpen.set(false);
      this.successMessage.set('انحفظ الشريك.');
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر حفظ الشريك.');
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleActive(partner: PartnerDto): Promise<void> {
    const action = partner.isActive ? 'إيقاف' : 'تفعيل';
    if (!confirm(`${action} ${partner.fullName}؟ ${partner.isActive ? '(ما بياخد نصيب بالكشوف الجاية، ورصيده بيضل)' : ''}`)) return;
    this.clearMessages();
    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Partners, PartnersOperation.SetActive, { isActive: !partner.isActive }, { id: partner.id })
      );
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? `تعذّر ${action} الشريك.`);
    }
  }

  async openLedger(partner: PartnerDto): Promise<void> {
    this.clearMessages();
    try {
      this.ledger.set(await firstValueFrom(
        this.apiClient.get<PartnerLedgerDto>(ApiController.Partners, PartnersOperation.Ledger, { id: partner.id })
      ));
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر جلب كشف الحساب.');
    }
  }

  closeLedger(): void {
    this.ledger.set(null);
  }

  // ===== تحقق الشريك بالكاشير (29/9/2026) =====

  /** كرت باركود جديد - بيلغي القديم. التفعيل نفسه من صفحة الإعدادات ("التحقق بباركود الشريك"). */
  async issueBarcode(partner: PartnerDto): Promise<void> {
    const warning = partner.hasCashierBarcode ? '\nالكرت القديم بيبطل يشتغل.' : '';
    if (!confirm(`إصدار باركود شخصي لـ${partner.fullName} (للتحقق وقت السحب من الكاشير)؟${warning}`)) return;
    this.clearMessages();
    try {
      const result = await firstValueFrom(this.apiClient.post<{ barcode: string; issuedAtUtc: string }>(
        ApiController.Partners, PartnersOperation.CashierBarcode, {}, { id: partner.id }));
      const rawSvg = code39Svg(result.barcode, 2, 90);
      this.issuedCard.set({
        partnerName: partner.fullName,
        barcode: result.barcode,
        rawSvg,
        svg: this.sanitizer.bypassSecurityTrustHtml(rawSvg)
      });
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر إصدار الباركود.');
    }
  }

  async revokeBarcode(partner: PartnerDto): Promise<void> {
    if (!confirm(`إلغاء باركود ${partner.fullName}؟ (كرت ضايع - ما عاد يسحب فيه)`)) return;
    this.clearMessages();
    try {
      await firstValueFrom(this.apiClient.delete(ApiController.Partners, PartnersOperation.CashierBarcode, { id: partner.id }));
      this.successMessage.set('انلغى الباركود.');
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر إلغاء الباركود.');
    }
  }

  closeIssuedCard(): void {
    this.issuedCard.set(null);
  }

  /** طباعة الكرت بنافذة لحالها (الباركود + الاسم + الأرقام للإدخال اليدوي). */
  printIssuedCard(): void {
    const card = this.issuedCard();
    if (!card) return;
    const win = window.open('', '_blank', 'width=520,height=360');
    if (!win) {
      this.errorMessage.set('المتصفح منع نافذة الطباعة - اسمح بالنوافذ المنبثقة لهالموقع.');
      return;
    }
    const name = card.partnerName.replace(/[<>&]/g, '');
    win.document.write(
      `<!doctype html><html dir="rtl"><head><meta charset="utf-8"><title>كرت ${name}</title>` +
      `<style>body{font-family:sans-serif;text-align:center;margin:24px}.digits{font-family:monospace;font-size:18px;letter-spacing:2px}</style>` +
      `</head><body><h3>${name} - تحقق سحب شريك</h3>${card.rawSvg}<p class="digits">${card.barcode}</p>` +
      `<p style="font-size:12px">احتفظ فيه معك - اللي معه الكرت بيقدر يسحب باسمك من الصندوق.</p></body></html>`);
    win.document.close();
    win.focus();
    win.print();
  }

  // ===== الكشوف =====

  async generateStatement(): Promise<void> {
    const [year, month] = this.statementMonth.split('-').map(Number);
    if (!year || !month) return;
    const exists = this.statements().some(s => s.year === year && s.month === month);
    if (exists && !confirm(`كشف ${month}/${year} موجود - إعادة إصداره بتحسب الربح من جديد وبتستبدل الأنصبة. متأكد؟`)) return;

    this.submitting.set(true);
    this.clearMessages();
    try {
      const statement = await firstValueFrom(
        this.apiClient.post<PartnerStatementDto>(ApiController.Partners, PartnersOperation.GenerateStatement, { branchId: this.branchId, year, month })
      );
      this.selectedStatement.set(statement);
      this.successMessage.set(`نزل كشف ${month}/${year}.`);
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر إصدار الكشف.');
    } finally {
      this.submitting.set(false);
    }
  }

  async openStatement(summary: PartnerStatementSummaryDto): Promise<void> {
    this.clearMessages();
    try {
      this.selectedStatement.set(await firstValueFrom(
        this.apiClient.get<PartnerStatementDto>(ApiController.Partners, PartnersOperation.StatementById, { id: summary.id })
      ));
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر جلب الكشف.');
    }
  }

  // ===== السحوبات =====

  async recordWithdrawal(): Promise<void> {
    const partner = this.partners().find(p => p.id === this.withdrawalPartnerId);
    if (!partner || !this.withdrawalAmount || this.withdrawalAmount <= 0) {
      this.errorMessage.set('اختار الشريك واكتب مبلغ أكبر من صفر.');
      return;
    }

    const after = partner.currentBalance - this.withdrawalAmount;
    const source = this.withdrawalSource === 'Drawer' ? 'من الصندوق' : 'من جيبك (بينسجّلك مستحق)';
    const warn = after < 0 ? `\nتنبيه: رصيده بيصير ${after.toFixed(3)} (سلفة على الشهر الجاي).` : '';
    if (!confirm(`${partner.fullName} بيسحب ${this.withdrawalAmount.toFixed(3)} د.أ ${source}؟${warn}`)) return;

    this.submitting.set(true);
    this.clearMessages();
    try {
      await firstValueFrom(this.apiClient.post(ApiController.Partners, PartnersOperation.RecordWithdrawal, {
        partnerId: partner.id,
        amount: this.withdrawalAmount,
        source: this.withdrawalSource,
        notes: this.withdrawalNotes.trim() || null,
        occurredAtUtc: null,
        clientRequestId: crypto.randomUUID()
      }));
      this.successMessage.set(`انسجّل سحب ${partner.fullName}.`);
      this.withdrawalAmount = null;
      this.withdrawalNotes = '';
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تسجيل السحب.');
    } finally {
      this.submitting.set(false);
    }
  }

  // ===== المستحق لصاحب المحل =====

  async repay(receivable: OwnerReceivableDto): Promise<void> {
    const key = receivable.ownerUserId;
    const amount = this.repayAmount[key];
    const source = this.repaySource[key] ?? 'Drawer';
    if (!amount || amount <= 0 || amount > receivable.balance) {
      this.errorMessage.set(`المبلغ لازم يكون بين 0 و${receivable.balance.toFixed(3)}.`);
      return;
    }
    if (!confirm(`${receivable.ownerName} بيسترجع ${amount.toFixed(3)} د.أ ${source === 'Drawer' ? 'من الصندوق' : 'برّا الصندوق'}؟`)) return;

    this.submitting.set(true);
    this.clearMessages();
    try {
      await firstValueFrom(this.apiClient.post(ApiController.Partners, PartnersOperation.OwnerRepayment, {
        branchId: receivable.branchId,
        ownerUserId: receivable.ownerUserId,
        amount,
        source,
        notes: null,
        clientRequestId: crypto.randomUUID()
      }));
      this.repayAmount[key] = null;
      this.successMessage.set('انسجّل الاسترجاع.');
      await this.reloadAll();
    } catch (err) {
      this.errorMessage.set(this.errorDetail(err) ?? 'تعذّر تسجيل الاسترجاع.');
    } finally {
      this.submitting.set(false);
    }
  }

  // ===== مساعدات =====

  private clearMessages(): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  private errorDetail(err: unknown): string | null {
    return err && typeof err === 'object' && 'error' in err
      ? ((err as { error?: { detail?: string } }).error?.detail ?? null)
      : null;
  }
}
