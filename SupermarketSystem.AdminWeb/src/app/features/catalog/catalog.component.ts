import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from '../../core/api/api-client.service';
import { ApiController } from '../../core/api/api-controller.enum';
import { ProductCategoriesOperation, ProductsOperation } from '../../core/api/operations';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { BarcodeScannerComponent } from './barcode-scanner/barcode-scanner.component';

interface CategoryDto {
  id: string;
  name: string;
  parentCategoryId: string | null;
}

interface ProductDto {
  id: string;
  name: string;
  categoryId: string;
  status: number;
  isBatchTracked: boolean;
  suggestedRetailPrice: number | null;
  expectedShelfLifeDays: number | null;
  isComplimentaryAllowed: boolean;
  createdAtUtc: string;
}

interface ProductUnitDto {
  id: string;
  unitName: string;
  conversionFactorToBase: number;
  isBaseUnit: boolean;
}

interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

type Tab = 'products' | 'categories';

/**
 * آخر صفحة من دفعة Edit — نفس نمط المستخدمين/الموردين بالضبط، بس
 * بنموذجين منفصلين (منتج، تصنيف) بدل واحد، لأن الكيانين مختلفين كليًا.
 */
@Component({
  selector: 'app-catalog',
  standalone: true,
  imports: [CommonModule, FormsModule, PaginationComponent, BarcodeScannerComponent],
  templateUrl: './catalog.component.html',
  styleUrl: './catalog.component.css'
})
export class CatalogComponent implements OnInit {
  readonly activeTab = signal<Tab>('products');

  readonly products = signal<ProductDto[]>([]);
  readonly productsTotalCount = signal(0);
  readonly productsPageNumber = signal(1);
  readonly productsPageSize = signal(20);
  readonly categories = signal<CategoryDto[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly productFormOpen = signal(false);
  readonly isEditingProduct = signal(false);
  readonly categoryFormOpen = signal(false);
  readonly isEditingCategory = signal(false);
  readonly submitting = signal(false);
  readonly formError = signal<string | null>(null);

  editingProductId = '';
  productName = '';
  productCategoryId = '';
  productPrice: number | null = null;
  productExpectedShelfLifeDays: number | null = null;
  productIsBatchTracked = false;
  baseUnitName = 'حبة';
  baseUnitBarcode = '';

  editingCategoryId = '';
  categoryName = '';

  readonly scannerOpen = signal(false);
  readonly scanFoundProduct = signal<{ productId: string; productName: string; categoryName: string } | null>(null);

  /** يفرّق بين سياقي استخدام الماسح: 'newProduct' (الزر العام بأعلى الصفحة) أو 'addUnit' (داخل نموذج تعديل منتج). */
  private scanMode: 'newProduct' | 'addUnit' = 'newProduct';

  // === قسم إدارة الوحدات (داخل نموذج تعديل منتج فقط) ===
  readonly productUnits = signal<ProductUnitDto[]>([]);
  readonly addUnitFormOpen = signal(false);
  readonly addUnitSubmitting = signal(false);
  readonly addUnitError = signal<string | null>(null);
  newUnitName = '';
  newUnitConversionFactor: number | null = null;
  newUnitBarcode = '';

  constructor(private readonly apiClient: ApiClient) {}

  ngOnInit(): void {
    this.loadAll();
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  onProductsPageChanged(event: { pageNumber: number; pageSize: number }): void {
    this.productsPageNumber.set(event.pageNumber);
    this.productsPageSize.set(event.pageSize);
    this.loadAll();
  }

  categoryNameOf(id: string): string {
    return this.categories().find(c => c.id === id)?.name ?? '—';
  }

  private async loadAll(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [productsResult, categoriesResult] = await Promise.all([
        firstValueFrom(this.apiClient.get<PagedResult<ProductDto>>(ApiController.Products, ProductsOperation.List, undefined, {
          pageNumber: this.productsPageNumber(),
          pageSize: this.productsPageSize()
        })),
        firstValueFrom(this.apiClient.get<PagedResult<CategoryDto>>(ApiController.ProductCategories, ProductCategoriesOperation.List))
      ]);

      this.products.set(productsResult.items);
      this.productsTotalCount.set(productsResult.totalCount);
      this.categories.set(categoriesResult.items);

      if (categoriesResult.items.length > 0 && !this.productCategoryId) {
        this.productCategoryId = categoriesResult.items[0].id;
      }
    } catch {
      this.errorMessage.set('تعذّر تحميل الكتالوج.');
    } finally {
      this.loading.set(false);
    }
  }

  // === المنتج ===

  openCreateProductForm(): void {
    this.isEditingProduct.set(false);
    this.productFormOpen.set(true);
    this.formError.set(null);
  }

  openEditProductForm(product: ProductDto): void {
    this.isEditingProduct.set(true);
    this.editingProductId = product.id;
    this.productName = product.name;
    this.productCategoryId = product.categoryId;
    this.productPrice = product.suggestedRetailPrice;
    this.productExpectedShelfLifeDays = product.expectedShelfLifeDays;
    this.productFormOpen.set(true);
    this.formError.set(null);
    this.addUnitFormOpen.set(false);
    this.loadProductUnits(product.id);
  }

  private async loadProductUnits(productId: string): Promise<void> {
    try {
      const units = await firstValueFrom(
        this.apiClient.get<ProductUnitDto[]>(ApiController.Products, ProductsOperation.GetUnits, { productId })
      );
      this.productUnits.set(units);
    } catch {
      this.productUnits.set([]);
    }
  }

  closeProductForm(): void {
    this.productFormOpen.set(false);
    this.editingProductId = '';
    this.productName = '';
    this.productPrice = null;
    this.productExpectedShelfLifeDays = null;
    this.productIsBatchTracked = false;
    this.baseUnitName = 'حبة';
    this.baseUnitBarcode = '';
    this.productUnits.set([]);
    this.addUnitFormOpen.set(false);
    this.newUnitName = '';
    this.newUnitConversionFactor = null;
    this.newUnitBarcode = '';
  }

  async submitProduct(): Promise<void> {
    if (!this.productName.trim() || !this.productCategoryId) {
      this.formError.set('عبّي كل الحقول المطلوبة.');
      return;
    }
    if (!this.isEditingProduct() && !this.baseUnitName.trim()) {
      this.formError.set('الوحدة الأساسية مطلوبة عند الإنشاء.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      if (this.isEditingProduct()) {
        await firstValueFrom(
          this.apiClient.put(ApiController.Products, ProductsOperation.Update, {
            name: this.productName.trim(),
            categoryId: this.productCategoryId,
            suggestedRetailPrice: this.productPrice,
            expectedShelfLifeDays: this.productExpectedShelfLifeDays
          }, { productId: this.editingProductId })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post(ApiController.Products, ProductsOperation.Create, {
            name: this.productName.trim(),
            description: null,
            categoryId: this.productCategoryId,
            isBatchTracked: this.productIsBatchTracked,
            suggestedRetailPrice: this.productPrice,
            expectedShelfLifeDays: this.productExpectedShelfLifeDays,
            units: [{ unitName: this.baseUnitName.trim(), conversionFactorToBase: 1, isBaseUnit: true }],
            barcodes: this.baseUnitBarcode.trim()
              ? [{ barcodeValue: this.baseUnitBarcode.trim(), unitName: this.baseUnitName.trim() }]
              : []
          })
        );
      }

      this.closeProductForm();
      await this.loadAll();
    } catch {
      this.formError.set(this.isEditingProduct() ? 'تعذّر تعديل المنتج.' : 'تعذّر إنشاء المنتج.');
    } finally {
      this.submitting.set(false);
    }
  }

  // === التصنيف ===

  openCreateCategoryForm(): void {
    this.isEditingCategory.set(false);
    this.categoryFormOpen.set(true);
    this.formError.set(null);
  }

  openEditCategoryForm(category: CategoryDto): void {
    this.isEditingCategory.set(true);
    this.editingCategoryId = category.id;
    this.categoryName = category.name;
    this.categoryFormOpen.set(true);
    this.formError.set(null);
  }

  closeCategoryForm(): void {
    this.categoryFormOpen.set(false);
    this.editingCategoryId = '';
    this.categoryName = '';
  }

  async submitCategory(): Promise<void> {
    if (!this.categoryName.trim()) {
      this.formError.set('اسم التصنيف مطلوب.');
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    try {
      if (this.isEditingCategory()) {
        await firstValueFrom(
          this.apiClient.put(ApiController.ProductCategories, ProductCategoriesOperation.Update, {
            name: this.categoryName.trim()
          }, { categoryId: this.editingCategoryId })
        );
      } else {
        await firstValueFrom(
          this.apiClient.post(ApiController.ProductCategories, ProductCategoriesOperation.Create, {
            name: this.categoryName.trim(),
            parentCategoryId: null
          })
        );
      }

      this.closeCategoryForm();
      await this.loadAll();
    } catch {
      this.formError.set(this.isEditingCategory() ? 'تعذّر تعديل التصنيف.' : 'تعذّر إنشاء التصنيف.');
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleComplimentaryAllowed(product: ProductDto): Promise<void> {
    const newValue = !product.isComplimentaryAllowed;

    try {
      await firstValueFrom(
        this.apiClient.post(
          ApiController.Products,
          ProductsOperation.SetComplimentaryAllowed,
          { allowed: newValue },
          { productId: product.id }
        )
      );
      product.isComplimentaryAllowed = newValue;
    } catch {
      this.errorMessage.set(`تعذّر تحديث "${product.name}".`);
    }
  }

  // === مسح الباركود بالكاميرا ===

  openScanner(mode: 'newProduct' | 'addUnit' = 'newProduct'): void {
    this.scanMode = mode;
    this.scanFoundProduct.set(null);
    this.scannerOpen.set(true);
  }

  closeScanner(): void {
    this.scannerOpen.set(false);
  }

  /**
   * سياق "منتج جديد": امسح أول، نتحقق فورًا هل موجود، نفتح نموذج جاهز.
   * سياق "إضافة وحدة" (داخل نموذج تعديل منتج موجود): نفس التحقق، بس
   * لو موجود بمنتج *آخر*، نوريه بوضوح ("مسجَّل أصلًا لمنتج آخر") بدل ما
   * نخلي الطلب يترفض بصمت بـ409 وقت الحفظ. لو مش موجود، نعبّي حقل
   * الباركود بنموذج إضافة الوحدة مباشرة.
   */
  async onBarcodeScanned(barcodeValue: string): Promise<void> {
    this.scannerOpen.set(false);

    if (this.scanMode === 'addUnit') {
      try {
        const existing = await firstValueFrom(
          this.apiClient.get<{ productId: string; productName: string; categoryName: string }>(
            ApiController.Products, ProductsOperation.GetByBarcode, { barcodeValue }
          )
        );
        this.addUnitError.set(`هذا الباركود مسجَّل أصلًا لمنتج آخر: "${existing.productName}".`);
      } catch {
        // 404 = باركود جديد، المسار الطبيعي.
        this.newUnitBarcode = barcodeValue;
        this.addUnitError.set(null);
      }
      return;
    }

    try {
      const existing = await firstValueFrom(
        this.apiClient.get<{ productId: string; productName: string; categoryName: string }>(
          ApiController.Products, ProductsOperation.GetByBarcode, { barcodeValue }
        )
      );
      this.scanFoundProduct.set(existing);
    } catch {
      this.openCreateProductForm();
      this.baseUnitBarcode = barcodeValue;
    }
  }

  closeFoundProductNotice(): void {
    this.scanFoundProduct.set(null);
  }

  // === إدارة الوحدات (داخل نموذج تعديل منتج) ===

  openAddUnitForm(): void {
    this.addUnitFormOpen.set(true);
    this.addUnitError.set(null);
    this.newUnitName = '';
    this.newUnitConversionFactor = null;
    this.newUnitBarcode = '';
  }

  closeAddUnitForm(): void {
    this.addUnitFormOpen.set(false);
  }

  async submitNewUnit(): Promise<void> {
    if (!this.newUnitName.trim() || !this.newUnitConversionFactor || this.newUnitConversionFactor <= 0) {
      this.addUnitError.set('اسم الوحدة ومعامل تحويل موجب مطلوبان.');
      return;
    }

    this.addUnitSubmitting.set(true);
    this.addUnitError.set(null);

    try {
      await firstValueFrom(
        this.apiClient.post(ApiController.Products, ProductsOperation.AddUnit, {
          unitName: this.newUnitName.trim(),
          conversionFactorToBase: this.newUnitConversionFactor,
          barcodeValue: this.newUnitBarcode.trim() || null
        }, { productId: this.editingProductId })
      );

      this.addUnitFormOpen.set(false);
      await this.loadProductUnits(this.editingProductId);
    } catch (err: unknown) {
      const message =
        err && typeof err === 'object' && 'error' in err
          ? (err as { error?: { detail?: string } }).error?.detail
          : null;
      this.addUnitError.set(message ?? 'تعذّر إضافة الوحدة.');
    } finally {
      this.addUnitSubmitting.set(false);
    }
  }
}
