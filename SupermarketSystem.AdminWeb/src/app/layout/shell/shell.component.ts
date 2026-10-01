import { Component, DestroyRef, ElementRef, HostListener, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { ThemeService } from '../../core/theme/theme.service';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { CashierSyncOperation, NotificationsOperation, ProductsOperation, SuppliersOperation, PurchaseInvoicesOperation } from '../../core/api/operations';
import { NAV_GROUPS, NAV_ICONS, NAV_ITEMS, NavGroupId, NavItem } from '../../shared/models/nav-item';
import { normalizeArabic } from '../../shared/components/select-filter/select-filter.component';

interface NavSection {
  id: NavGroupId;
  label: string | null;
  items: NavItem[];
}

const COLLAPSED_GROUPS_STORAGE_KEY = 'nav.collapsedGroups';
const SIDEBAR_COLLAPSED_STORAGE_KEY = 'nav.sidebarCollapsed';
const NAV_ORDER_STORAGE_KEY = 'nav.order';
/** آخر اسم محل معروف - صفحة الدخول بتقرأه (login.component.ts). */
export const STORE_NAME_STORAGE_KEY = 'store.name';

/** ترتيب شخصي للقائمة (لكل متصفح): ترتيب المجموعات، وترتيب الصفحات جوّا كل مجموعة. */
interface NavOrder {
  groups: NavGroupId[];
  items: Partial<Record<NavGroupId, string[]>>;
}

/** بترتّب حسب قائمة مفضّلة؛ اللي مش بالقائمة (صفحة جديدة مثلًا) بيضل بترتيبه الافتراضي بالآخر. */
function orderBy<T>(list: T[], key: (item: T) => string, preferred: string[] | undefined): T[] {
  if (!preferred?.length) {
    return list;
  }
  const rank = (item: T) => {
    const index = preferred.indexOf(key(item));
    return index < 0 ? preferred.length + list.indexOf(item) : index;
  };
  return [...list].sort((a, b) => rank(a) - rank(b));
}

interface SearchResultItem {
  type: 'page' | 'product' | 'supplier' | 'invoice';
  typeLabel: string;
  id: string;
  label: string;
  sublabel: string;
  route: string;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.css'
})
export class ShellComponent {
  readonly navItems = computed(() =>
    NAV_ITEMS.filter(item => !item.requiredPermission || this.permissionsService.has(item.requiredPermission))
  );

  /** الجرس بس لمين عنده Notifications.View (الكاشير بلا تنبيهات - قرار صاحب المشروع 1/10/2026 "الكاشير ما يشوف اشي"). */
  readonly canSeeAlerts = computed(() => this.permissionsService.loaded() && this.permissionsService.has('Notifications.View'));

  /** ترتيب شخصي من المستخدم (زر "ترتيب القائمة")؛ null = الترتيب الافتراضي. */
  readonly navOrder = signal<NavOrder | null>(this.readNavOrder());

  /** المجموعات الظاهرة بس (مجموعة كل عناصرها مخفية بالصلاحيات ما بتطلع عنوانها فاضي)، بالترتيب الشخصي لو في. */
  readonly navSections = computed<NavSection[]>(() => {
    const visible = this.navItems();
    const order = this.navOrder();
    const groups = orderBy(NAV_GROUPS, g => g.id, order?.groups);
    return groups
      .map(g => ({
        id: g.id,
        label: g.label,
        items: orderBy(visible.filter(i => i.group === g.id), i => i.id, order?.items[g.id])
      }))
      .filter(section => section.items.length > 0);
  });

  /** وضع ترتيب القائمة: أسهم ↑↓ بدل الروابط، وزر إعادة الترتيب الافتراضي. */
  readonly arrangeMode = signal(false);

  /** القائمة الجانبية مطويّة لأيقونات بس (كمبيوتر) - تفضيل لكل متصفح. */
  readonly sidebarCollapsed = signal<boolean>(this.readFlag(SIDEBAR_COLLAPSED_STORAGE_KEY));

  @ViewChild('searchInput') private searchInput?: ElementRef<HTMLInputElement>;

  readonly icons = NAV_ICONS;

  /** مفضّلة شخصية لكل متصفح - لو التخزين مش متاح بتفتح كل المجموعات عادي. */
  readonly collapsedGroups = signal<Set<NavGroupId>>(this.readCollapsedGroups());

  /** اسم المحل من الإعدادات (نفس اللي بينطبع على فاتورة الكاشير) - بدل اسم تجريبي ثابت. */
  readonly storeName = signal<string | null>(null);

  readonly userInitials = computed(() => {
    const name = (this.authService.currentUserFullName() ?? '').trim();
    if (!name) {
      return '؟';
    }
    // من الاسم الفعلي للمستخدم (كانت "م.س" ثابتة): أول حرفين من أول كلمتين، أو أول حرف لكلمة وحدة.
    const parts = name.split(/\s+/).filter(Boolean);
    return parts.length > 1 ? `${parts[0][0]}.${parts[1][0]}` : parts[0][0].toLocaleUpperCase();
  });

  readonly drawerOpen = signal(false);
  readonly searchQuery = signal('');
  readonly hasQuery = computed(() => this.searchQuery().length > 0);

  readonly searchResults = signal<SearchResultItem[]>([]);
  /** صفحات مطابقة للبحث - محلية وفورية (من أول حرف)، بتطلع قبل نتائج المنتجات والفواتير. */
  readonly pageResults = signal<SearchResultItem[]>([]);
  readonly allResults = computed(() => [...this.pageResults(), ...this.searchResults()]);
  readonly searchOpen = signal(false);
  readonly searching = signal(false);
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

  /** عدّاد الجرس: غير المقروء (عالية + متوسطة)؛ critical = في عالي الأولوية (الشارة بتصير حمرا). */
  readonly unreadAlerts = signal<{ count: number; critical: boolean }>({ count: 0, critical: false });
  private readonly destroyRef = inject(DestroyRef);

  constructor(
    readonly theme: ThemeService,
    readonly authService: AuthService,
    readonly permissionsService: PermissionsService,
    private readonly apiClient: ApiClient,
    private readonly router: Router
  ) {
    if (!this.permissionsService.loaded()) {
      this.permissionsService.load();
    }

    this.router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => {
      this.drawerOpen.set(false);
      this.searchOpen.set(false);
      this.expandGroupOfCurrentRoute();
      void this.refreshUnreadAlerts();
    });

    this.loadStoreName();
    void this.refreshUnreadAlerts();
    const pollHandle = setInterval(() => void this.refreshUnreadAlerts(), 60_000);
    this.destroyRef.onDestroy(() => clearInterval(pollHandle));
  }

  /** بلا صلاحية Notifications.View (403) أو خطأ شبكة = الجرس بلا عدّاد، بصمت. */
  async refreshUnreadAlerts(): Promise<void> {
    if (this.permissionsService.loaded() && !this.permissionsService.has('Notifications.View')) {
      this.unreadAlerts.set({ count: 0, critical: false });
      return;
    }
    try {
      const s = await firstValueFrom(this.apiClient.get<{ unreadCritical: number; unreadWarning: number; unreadInfo: number }>(
        ApiController.Notifications, NotificationsOperation.Summary));
      this.unreadAlerts.set({ count: (s?.unreadCritical ?? 0) + (s?.unreadWarning ?? 0), critical: (s?.unreadCritical ?? 0) > 0 });
    } catch {
      this.unreadAlerts.set({ count: 0, critical: false });
    }
  }

  isGroupCollapsed(groupId: NavGroupId): boolean {
    return this.collapsedGroups().has(groupId);
  }

  toggleGroup(groupId: NavGroupId): void {
    const next = new Set(this.collapsedGroups());
    if (next.has(groupId)) {
      next.delete(groupId);
    } else {
      next.add(groupId);
    }
    this.collapsedGroups.set(next);
    this.writeCollapsedGroups(next);
  }

  /** الصفحة المفتوحة حاليًا ما بتضل مخبّاية جوّا مجموعة مطويّة. */
  private expandGroupOfCurrentRoute(): void {
    const path = this.router.url.split('?')[0];
    const active = NAV_ITEMS
      .filter(i => i.route === '/' ? path === '/' : path === i.route || path.startsWith(i.route + '/'))
      .sort((a, b) => b.route.length - a.route.length)[0];

    if (active && this.collapsedGroups().has(active.group)) {
      this.toggleGroup(active.group);
    }
  }

  private readCollapsedGroups(): Set<NavGroupId> {
    try {
      const raw = localStorage.getItem(COLLAPSED_GROUPS_STORAGE_KEY);
      return new Set(raw ? (JSON.parse(raw) as NavGroupId[]) : []);
    } catch {
      return new Set();
    }
  }

  private writeCollapsedGroups(groups: Set<NavGroupId>): void {
    try {
      localStorage.setItem(COLLAPSED_GROUPS_STORAGE_KEY, JSON.stringify([...groups]));
    } catch {
      /* تفضيل شكلي بس - فشل الحفظ ما بيأثر على شي. */
    }
  }

  private async loadStoreName(): Promise<void> {
    try {
      const branding = await firstValueFrom(
        this.apiClient.get<{ storeName: string | null }>(ApiController.CashierSync, CashierSyncOperation.StoreBranding)
      );
      this.storeName.set(branding?.storeName?.trim() || null);
      // صفحة الدخول بتعرضه المرة الجاية (بلا endpoint عام بلا دخول).
      try {
        if (branding?.storeName?.trim()) localStorage.setItem(STORE_NAME_STORAGE_KEY, branding.storeName.trim());
      } catch {
        /* تخزين المتصفح ممنوع - صفحة الدخول بتعرض الاسم العام. */
      }
    } catch {
      /* بلا صلاحية Sales.Create أو بلا اتصال - بيضل الاسم العام. */
    }
  }

  toggleDrawer(): void {
    this.drawerOpen.update(open => !open);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  clearQuery(): void {
    this.searchQuery.set('');
    this.searchResults.set([]);
    this.pageResults.set([]);
    this.searchOpen.set(false);
  }

  /** بحث بأسماء الصفحات المسموحة للمستخدم (بلا حساسية همزة/تاء مربوطة) + اسم مجموعتها. */
  private matchPages(query: string): SearchResultItem[] {
    const q = normalizeArabic(query);
    if (!q) {
      return [];
    }
    return this.navItems()
      .filter(item => normalizeArabic(item.label).includes(q))
      .slice(0, 6)
      .map(item => ({
        type: 'page' as const,
        typeLabel: 'صفحة',
        id: item.id,
        label: item.label,
        sublabel: NAV_GROUPS.find(g => g.id === item.group)?.label ?? '',
        route: item.route
      }));
  }

  /** Enter بخانة البحث = أول نتيجة (غالبًا الصفحة المطلوبة). */
  onSearchEnter(): void {
    const first = this.allResults()[0];
    if (first) {
      this.goToResult(first);
    }
  }

  /** Ctrl+K (أو ⌘K) من أي مكان = خانة البحث. */
  @HostListener('document:keydown', ['$event'])
  onGlobalKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.searchInput?.nativeElement.focus();
      this.searchInput?.nativeElement.select();
    }
  }

  // === طيّ القائمة وترتيبها ===

  toggleSidebarCollapsed(): void {
    const next = !this.sidebarCollapsed();
    this.sidebarCollapsed.set(next);
    this.writeFlag(SIDEBAR_COLLAPSED_STORAGE_KEY, next);
    if (next) {
      this.arrangeMode.set(false);
    }
  }

  toggleArrangeMode(): void {
    this.arrangeMode.update(on => !on);
  }

  /** تحريك صفحة لفوق/لتحت جوّا مجموعتها. */
  moveItem(section: NavSection, index: number, delta: -1 | 1): void {
    const ids = section.items.map(i => i.id);
    const target = index + delta;
    if (target < 0 || target >= ids.length) {
      return;
    }
    [ids[index], ids[target]] = [ids[target], ids[index]];
    const current = this.currentOrder();
    this.saveNavOrder({ ...current, items: { ...current.items, [section.id]: ids } });
  }

  /** تحريك مجموعة كاملة لفوق/لتحت ("الرئيسية" بتضل أول دايمًا). */
  moveGroup(sectionIndex: number, delta: -1 | 1): void {
    const ids = this.navSections().map(s => s.id);
    const target = sectionIndex + delta;
    if (target < 0 || target >= ids.length || ids[target] === 'main' || ids[sectionIndex] === 'main') {
      return;
    }
    [ids[sectionIndex], ids[target]] = [ids[target], ids[sectionIndex]];
    this.saveNavOrder({ ...this.currentOrder(), groups: ids });
  }

  resetNavOrder(): void {
    this.navOrder.set(null);
    try {
      localStorage.removeItem(NAV_ORDER_STORAGE_KEY);
    } catch {
      /* تفضيل شكلي بس. */
    }
  }

  private currentOrder(): NavOrder {
    return this.navOrder() ?? { groups: this.navSections().map(s => s.id), items: {} };
  }

  private saveNavOrder(order: NavOrder): void {
    this.navOrder.set(order);
    try {
      localStorage.setItem(NAV_ORDER_STORAGE_KEY, JSON.stringify(order));
    } catch {
      /* تفضيل شكلي بس - فشل الحفظ ما بيأثر على شي. */
    }
  }

  private readNavOrder(): NavOrder | null {
    try {
      const raw = localStorage.getItem(NAV_ORDER_STORAGE_KEY);
      const parsed = raw ? (JSON.parse(raw) as NavOrder) : null;
      return parsed && Array.isArray(parsed.groups) && typeof parsed.items === 'object' ? parsed : null;
    } catch {
      return null;
    }
  }

  private readFlag(key: string): boolean {
    try {
      return localStorage.getItem(key) === 'true';
    } catch {
      return false;
    }
  }

  private writeFlag(key: string, value: boolean): void {
    try {
      localStorage.setItem(key, String(value));
    } catch {
      /* تفضيل شكلي بس. */
    }
  }

  /**
   * بحث عام حقيقي — يبحث بالمنتجات والموردين وفواتير الشراء بالتوازي،
   * عبر نفس معامل Search الموجود أصلًا بكل استعلامات الباك إند
   * (PagedRequest.Search). مُحدَّد زمنيًا (debounce) لتفادي طلب لكل حرف
   * يكتبه المستخدم.
   *
   * كل قسم بحث محكوم بصلاحية المستخدم عليه — بحث بمنتجات لمستخدم بلا
   * Catalog.Manage كان رح يترفض بـ403 من الباك إند أصلًا (نفس الحماية
   * المطبَّقة بكل مكان)؛ هون بس نتجنّب الطلب الفاشل من الأساس.
   */
  onSearchInput(query: string): void {
    this.searchQuery.set(query);

    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    const trimmed = query.trim();
    this.pageResults.set(this.matchPages(trimmed));
    if (this.pageResults().length > 0) {
      // الصفحات فورية - ما بتستنّى تأجيل بحث المنتجات والفواتير.
      this.searchOpen.set(true);
    }

    if (trimmed.length < 2) {
      this.searchResults.set([]);
      this.searchOpen.set(this.pageResults().length > 0);
      return;
    }

    this.searchDebounceHandle = setTimeout(() => this.runSearch(trimmed), 350);
  }

  private async runSearch(query: string): Promise<void> {
    this.searching.set(true);
    this.searchOpen.set(true);

    const tasks: Promise<SearchResultItem[]>[] = [];

    if (this.permissionsService.has('Catalog.Manage')) {
      tasks.push(this.searchProducts(query));
    }
    if (this.permissionsService.has('Suppliers.Manage')) {
      tasks.push(this.searchSuppliers(query));
    }
    if (this.permissionsService.has('Purchasing.Create')) {
      tasks.push(this.searchPurchaseInvoices(query));
    }

    try {
      const resultGroups = await Promise.all(tasks);
      this.searchResults.set(resultGroups.flat());
    } catch {
      this.searchResults.set([]);
    } finally {
      this.searching.set(false);
    }
  }

  private async searchProducts(query: string): Promise<SearchResultItem[]> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: { id: string; name: string }[] }>(
          ApiController.Products, ProductsOperation.List, undefined, { search: query, pageSize: 5 }
        )
      );
      return result.items.map(p => ({
        type: 'product' as const, typeLabel: 'منتج', id: p.id, label: p.name, sublabel: 'دليل الأصناف', route: '/catalog'
      }));
    } catch {
      return [];
    }
  }

  private async searchSuppliers(query: string): Promise<SearchResultItem[]> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: { id: string; name: string }[] }>(
          ApiController.Suppliers, SuppliersOperation.List, undefined, { search: query, pageSize: 5 }
        )
      );
      return result.items.map(s => ({
        type: 'supplier' as const, typeLabel: 'مورد', id: s.id, label: s.name, sublabel: 'الموردين', route: '/suppliers'
      }));
    } catch {
      return [];
    }
  }

  private async searchPurchaseInvoices(query: string): Promise<SearchResultItem[]> {
    try {
      const result = await firstValueFrom(
        this.apiClient.get<{ items: { id: string; invoiceNumber: string; supplierName: string }[] }>(
          ApiController.PurchaseInvoices, PurchaseInvoicesOperation.List, undefined, { search: query, pageSize: 5 }
        )
      );
      return result.items.map(i => ({
        type: 'invoice' as const, typeLabel: 'فاتورة شراء', id: i.id, label: i.invoiceNumber, sublabel: i.supplierName, route: '/purchases'
      }));
    } catch {
      return [];
    }
  }

  goToResult(result: SearchResultItem): void {
    this.router.navigateByUrl(result.route);
    this.clearQuery();
  }

  /** يقفل القائمة عند أي نقرة خارج مربع البحث نفسه (اللي بيوقف الانتشار بـstopPropagation). */
  @HostListener('document:click')
  onDocumentClick(): void {
    this.searchOpen.set(false);
  }

  get themeLabel(): string {
    return this.theme.mode() === 'dark' ? 'الوضع النهاري' : 'الوضع الليلي';
  }

  async logout(): Promise<void> {
    await this.authService.logout();
    this.permissionsService.reset();
    this.router.navigateByUrl('/login');
  }
}
