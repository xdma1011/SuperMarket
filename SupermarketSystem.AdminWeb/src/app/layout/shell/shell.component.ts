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
import { ProductsOperation, SuppliersOperation, PurchaseInvoicesOperation } from '../../core/api/operations';
import { NAV_ITEMS } from '../../shared/models/nav-item';

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
    });
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
