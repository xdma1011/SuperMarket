import { Component, signal, computed, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { ThemeService } from '../../core/theme/theme.service';
import { AuthService } from '../../core/services/auth.service';
import { PermissionsService } from '../../core/services/permissions.service';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { CashierSyncOperation, ProductsOperation, SuppliersOperation, PurchaseInvoicesOperation } from '../../core/api/operations';
import { NAV_GROUPS, NAV_ICONS, NAV_ITEMS, NavGroupId, NavItem } from '../../shared/models/nav-item';

interface NavSection {
  id: NavGroupId;
  label: string | null;
  items: NavItem[];
}

const COLLAPSED_GROUPS_STORAGE_KEY = 'nav.collapsedGroups';

interface SearchResultItem {
  type: 'product' | 'supplier' | 'invoice';
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

  /** المجموعات الظاهرة بس (مجموعة كل عناصرها مخفية بالصلاحيات ما بتطلع عنوانها فاضي). */
  readonly navSections = computed<NavSection[]>(() => {
    const visible = this.navItems();
    return NAV_GROUPS
      .map(g => ({ id: g.id, label: g.label, items: visible.filter(i => i.group === g.id) }))
      .filter(section => section.items.length > 0);
  });

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
    const parts = name.split(/\s+/).filter(Boolean);
    return parts.length > 1 ? `${parts[0][0]}.${parts[1][0]}` : parts[0].slice(0, 2);
  });

  readonly drawerOpen = signal(false);
  readonly searchQuery = signal('');
  readonly hasQuery = computed(() => this.searchQuery().length > 0);

  readonly searchResults = signal<SearchResultItem[]>([]);
  readonly searchOpen = signal(false);
  readonly searching = signal(false);
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

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
    });

    this.loadStoreName();
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
    this.searchOpen.set(false);
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
    if (trimmed.length < 2) {
      this.searchResults.set([]);
      this.searchOpen.set(false);
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
        type: 'product' as const, typeLabel: 'منتج', id: p.id, label: p.name, sublabel: 'الكتالوج', route: '/catalog'
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
