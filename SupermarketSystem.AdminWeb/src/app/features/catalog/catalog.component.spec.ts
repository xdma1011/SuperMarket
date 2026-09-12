import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { CatalogComponent } from './catalog.component';
import { ApiClient } from '../../core/api/api-client.service';

describe('CatalogComponent', () => {
  let fixture: ComponentFixture<CatalogComponent>;
  let component: CatalogComponent;
  let apiClientSpy: jasmine.SpyObj<ApiClient>;

  const sampleProduct = {
    id: 'p1', name: 'سكر', categoryId: 'c1', status: 1, isBatchTracked: false,
    suggestedRetailPrice: 5, expectedShelfLifeDays: null, isComplimentaryAllowed: false, createdAtUtc: ''
  };
  const sampleCategory = { id: 'c1', name: 'مواد غذائية', parentCategoryId: null };
  const sampleBranch = { id: 'b1', name: 'الرئيسي' };
  const sampleUnit = { id: 'u1', name: 'كيلوغرام', isActive: true };
  const sampleProductBranch = { productBranchId: 'pb1', branchId: 'b1', branchName: 'الرئيسي', sellingPrice: 5, minimumStock: null, maximumStock: null, isAvailableForSale: true };

  function mockLoadAllSuccess() {
    apiClientSpy.get.and.callFake(((controller: string) => {
      if (controller === 'products') return of({ items: [sampleProduct], totalCount: 1 });
      if (controller === 'product-categories') return of({ items: [sampleCategory], totalCount: 1 });
      if (controller === 'branches') return of({ items: [sampleBranch], totalCount: 1 });
      if (controller === 'units-of-measure') return of([sampleUnit]);
      return of({ items: [], totalCount: 0 });
    }) as unknown as typeof apiClientSpy.get);
  }

  beforeEach(async () => {
    apiClientSpy = jasmine.createSpyObj('ApiClient', ['get', 'post', 'put']);
    apiClientSpy.get.and.returnValue(of({ items: [], totalCount: 0 }));

    await TestBed.configureTestingModule({
      imports: [CatalogComponent],
      providers: [{ provide: ApiClient, useValue: apiClientSpy }]
    }).compileComponents();

    fixture = TestBed.createComponent(CatalogComponent);
    component = fixture.componentInstance;
  });

  it('يُنشأ المكوّن بنجاح، بتبويب "المنتجات" افتراضيًا', () => {
    expect(component).toBeTruthy();
    expect(component.activeTab()).toBe('products');
  });

  it('يحمّل المنتجات والتصنيفات والفروع ووحدات القياس معًا، ويختار أول تصنيف وفرع', async () => {
    mockLoadAllSuccess();

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.products().length).toBe(1);
    expect(component.categories().length).toBe(1);
    expect(component.productCategoryId).toBe('c1');
    expect(component.newBranchId).toBe('b1');
  });

  it('يعرض رسالة خطأ عربية واضحة عند فشل التحميل', async () => {
    apiClientSpy.get.and.returnValue(throwError(() => new Error('network')));

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.errorMessage()).toBe('تعذّر تحميل الكتالوج.');
  });

  describe('setTab', () => {
    it('يبدّل التبويب النشِط', () => {
      component.setTab('categories');
      expect(component.activeTab()).toBe('categories');
    });
  });

  describe('onProductsPageChanged', () => {
    it('يحدّث الصفحة ويعيد التحميل', () => {
      apiClientSpy.get.calls.reset();
      component.onProductsPageChanged({ pageNumber: 2, pageSize: 50 });
      expect(component.productsPageNumber()).toBe(2);
      expect(apiClientSpy.get).toHaveBeenCalled();
    });
  });

  describe('categoryNameOf', () => {
    it('يرجّع اسم التصنيف الصحيح', () => {
      component.categories.set([sampleCategory]);
      expect(component.categoryNameOf('c1')).toBe('مواد غذائية');
    });

    it('يرجّع "—" لمعرّف غير موجود', () => {
      expect(component.categoryNameOf('c999')).toBe('—');
    });
  });

  describe('المنتج - openCreateProductForm / openEditProductForm / closeProductForm', () => {
    it('openCreateProductForm يفتح بوضع إنشاء ويعبّي الوحدة الأساسية بأول وحدة متاحة', async () => {
      mockLoadAllSuccess();
      fixture.detectChanges();
      await fixture.whenStable();

      component.openCreateProductForm();

      expect(component.isEditingProduct()).toBeFalse();
      expect(component.productFormOpen()).toBeTrue();
      expect(component.baseUnitName).toBe('كيلوغرام');
    });

    it('openEditProductForm يعبّي الحقول ويحمّل الوحدات والفروع', async () => {
      apiClientSpy.get.and.callFake(((controller: string, operation: string) => {
        if (operation === '{productId}/units') return of([sampleUnit]);
        if (operation === '{productId}/branches') return of([sampleProductBranch]);
        return of({ items: [], totalCount: 0 });
      }) as unknown as typeof apiClientSpy.get);

      component.openEditProductForm(sampleProduct);
      await fixture.whenStable();

      expect(component.isEditingProduct()).toBeTrue();
      expect(component.editingProductId).toBe('p1');
      expect(component.productName).toBe('سكر');
      expect(component.productBranches().length).toBe(1);
    });

    it('closeProductForm يصفّر كل حقول النموذج والحالات الفرعية', () => {
      component.editingProductId = 'p1';
      component.productName = 'سكر';
      component.editingUnitBarcodeId = 'u1';
      component.editingBranchPriceValue = 10;

      component.closeProductForm();

      expect(component.productFormOpen()).toBeFalse();
      expect(component.editingProductId).toBe('');
      expect(component.productName).toBe('');
      expect(component.editingUnitBarcodeId).toBe('');
      expect(component.editingBranchPriceValue).toBeNull();
    });
  });

  describe('submitProduct', () => {
    it('يرفض بلا اسم أو تصنيف', async () => {
      component.productName = '';
      await component.submitProduct();
      expect(component.formError()).toBe('عبّي كل الحقول المطلوبة.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('يرفض الإنشاء بلا وحدة أساسية', async () => {
      component.productName = 'سكر';
      component.productCategoryId = 'c1';
      component.baseUnitName = '';

      await component.submitProduct();

      expect(component.formError()).toBe('الوحدة الأساسية مطلوبة عند الإنشاء.');
    });

    it('ينشئ منتجًا جديدًا (post) بوحدة أساسية وباركود اختياري', async () => {
      component.productName = 'سكر';
      component.productCategoryId = 'c1';
      component.baseUnitName = 'كيلوغرام';
      component.baseUnitBarcode = '123';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submitProduct();

      const [, , body] = apiClientSpy.post.calls.mostRecent().args;
      expect((body as Record<string, unknown>)['units']).toEqual([{ unitName: 'كيلوغرام', conversionFactorToBase: 1, isBaseUnit: true }]);
      expect((body as Record<string, unknown>)['barcodes']).toEqual([{ barcodeValue: '123', unitName: 'كيلوغرام' }]);
      expect(component.productFormOpen()).toBeFalse();
    });

    it('يرسل barcodes فاضية لو ما في باركود مُدخَل', async () => {
      component.productName = 'سكر';
      component.productCategoryId = 'c1';
      component.baseUnitName = 'كيلوغرام';
      component.baseUnitBarcode = '';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submitProduct();

      const [, , body] = apiClientSpy.post.calls.mostRecent().args;
      expect((body as Record<string, unknown>)['barcodes']).toEqual([]);
    });

    it('يعدّل منتجًا موجودًا (put) بمعرّفه الصحيح', async () => {
      component.openEditProductForm(sampleProduct);
      component.productName = 'اسم معدَّل';
      apiClientSpy.put.and.returnValue(of({}));

      await component.submitProduct();

      expect(apiClientSpy.put).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        jasmine.objectContaining({ name: 'اسم معدَّل' }),
        { productId: 'p1' }
      );
    });

    it('يعرض رسالة خطأ عربية مختلفة بين الإنشاء والتعديل عند الفشل', async () => {
      component.productName = 'سكر';
      component.productCategoryId = 'c1';
      component.baseUnitName = 'كيلو';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.submitProduct();

      expect(component.formError()).toBe('تعذّر إنشاء المنتج.');
    });
  });

  describe('التصنيف - openCreateCategoryForm / openEditCategoryForm / closeCategoryForm / submitCategory', () => {
    it('openEditCategoryForm يعبّي الاسم بوضع تعديل', () => {
      component.openEditCategoryForm(sampleCategory);
      expect(component.isEditingCategory()).toBeTrue();
      expect(component.categoryName).toBe('مواد غذائية');
    });

    it('closeCategoryForm يصفّر الحقول', () => {
      component.openEditCategoryForm(sampleCategory);
      component.closeCategoryForm();
      expect(component.categoryFormOpen()).toBeFalse();
      expect(component.categoryName).toBe('');
    });

    it('submitCategory يرفض بلا اسم', async () => {
      component.categoryName = '   ';
      await component.submitCategory();
      expect(component.formError()).toBe('اسم التصنيف مطلوب.');
    });

    it('submitCategory ينشئ تصنيفًا جديدًا (post)', async () => {
      component.categoryName = 'تصنيف جديد';
      apiClientSpy.post.and.returnValue(of({}));

      await component.submitCategory();

      expect(apiClientSpy.post).toHaveBeenCalled();
      expect(component.categoryFormOpen()).toBeFalse();
    });

    it('submitCategory يعدّل تصنيفًا موجودًا (put) بمعرّفه الصحيح', async () => {
      component.openEditCategoryForm(sampleCategory);
      component.categoryName = 'اسم جديد';
      apiClientSpy.put.and.returnValue(of({}));

      await component.submitCategory();

      expect(apiClientSpy.put).toHaveBeenCalledWith(jasmine.anything(), jasmine.anything(), { name: 'اسم جديد' }, { categoryId: 'c1' });
    });

    it('submitCategory يعرض رسالة خطأ مناسبة عند الفشل', async () => {
      component.categoryName = 'تصنيف';
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.submitCategory();

      expect(component.formError()).toBe('تعذّر إنشاء التصنيف.');
    });
  });

  describe('toggleComplimentaryAllowed', () => {
    it('يبدّل القيمة محليًا عند النجاح', async () => {
      const product = { ...sampleProduct, isComplimentaryAllowed: false };
      apiClientSpy.post.and.returnValue(of({}));

      await component.toggleComplimentaryAllowed(product);

      expect(product.isComplimentaryAllowed).toBeTrue();
    });

    it('يعرض رسالة خطأ عربية تحمل اسم المنتج عند الفشل، بلا تبديل القيمة', async () => {
      const product = { ...sampleProduct, isComplimentaryAllowed: false };
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.toggleComplimentaryAllowed(product);

      expect(component.errorMessage()).toBe('تعذّر تحديث "سكر".');
      expect(product.isComplimentaryAllowed).toBeFalse();
    });
  });

  describe('الماسح - openScanner / closeScanner / onBarcodeScanned / closeFoundProductNotice', () => {
    it('openScanner يفتح الماسح ويمسح scanFoundProduct السابق', () => {
      component.scanFoundProduct.set({ productId: 'p1', productName: 'سكر', categoryName: 'مواد غذائية' });

      component.openScanner('newProduct');

      expect(component.scannerOpen()).toBeTrue();
      expect(component.scanFoundProduct()).toBeNull();
    });

    it('closeScanner يغلق الماسح', () => {
      component.scannerOpen.set(true);
      component.closeScanner();
      expect(component.scannerOpen()).toBeFalse();
    });

    it('onBarcodeScanned بسياق newProduct: يعرض المنتج الموجود لو الباركود مطابق أصلًا', async () => {
      component.openScanner('newProduct');
      apiClientSpy.get.and.returnValue(of({ productId: 'p1', productName: 'سكر', categoryName: 'مواد غذائية' }));

      await component.onBarcodeScanned('123456');

      expect(component.scanFoundProduct()?.productName).toBe('سكر');
      expect(component.scannerOpen()).toBeFalse();
    });

    it('onBarcodeScanned بسياق newProduct: يفتح نموذج منتج جديد بالباركود معبّى لو غير موجود', async () => {
      component.openScanner('newProduct');
      apiClientSpy.get.and.returnValue(throwError(() => new Error('not found')));

      await component.onBarcodeScanned('999999');

      expect(component.productFormOpen()).toBeTrue();
      expect(component.baseUnitBarcode).toBe('999999');
    });

    it('onBarcodeScanned بسياق addUnit: يعرض تحذير لو الباركود مسجَّل لمنتج آخر', async () => {
      component.openScanner('addUnit');
      apiClientSpy.get.and.returnValue(of({ productId: 'p2', productName: 'منتج آخر', categoryName: 'أخرى' }));

      await component.onBarcodeScanned('123456');

      expect(component.addUnitError()).toBe('هذا الباركود مسجَّل أصلًا لمنتج آخر: "منتج آخر".');
    });

    it('onBarcodeScanned بسياق addUnit: يعبّي حقل الباركود لو غير موجود', async () => {
      component.openScanner('addUnit');
      apiClientSpy.get.and.returnValue(throwError(() => new Error('not found')));

      await component.onBarcodeScanned('999999');

      expect(component.newUnitBarcode).toBe('999999');
      expect(component.addUnitError()).toBeNull();
    });

    it('closeFoundProductNotice يمسح المنتج الموجود', () => {
      component.scanFoundProduct.set({ productId: 'p1', productName: 'سكر', categoryName: 'مواد غذائية' });
      component.closeFoundProductNotice();
      expect(component.scanFoundProduct()).toBeNull();
    });
  });

  describe('إدارة الوحدات - openAddUnitForm / submitNewUnit', () => {
    it('openAddUnitForm يعبّي أول وحدة قياس متاحة', async () => {
      mockLoadAllSuccess();
      fixture.detectChanges();
      await fixture.whenStable();

      component.openAddUnitForm();

      expect(component.addUnitFormOpen()).toBeTrue();
      expect(component.newUnitName).toBe('كيلوغرام');
    });

    it('submitNewUnit يرفض بلا اسم أو معامل تحويل موجب', async () => {
      component.newUnitName = '';
      component.newUnitConversionFactor = null;

      await component.submitNewUnit();

      expect(component.addUnitError()).toBe('اسم الوحدة ومعامل تحويل موجب مطلوبان.');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('submitNewUnit يضيف الوحدة بنجاح ويغلق النموذج', async () => {
      component.editingProductId = 'p1';
      component.newUnitName = 'صندوق';
      component.newUnitConversionFactor = 12;
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of([sampleUnit]));

      await component.submitNewUnit();

      expect(component.addUnitFormOpen()).toBeFalse();
    });

    it('submitNewUnit يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.newUnitName = 'صندوق';
      component.newUnitConversionFactor = 12;
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الباركود مستخدم مسبقًا.' } })));

      await component.submitNewUnit();

      expect(component.addUnitError()).toBe('الباركود مستخدم مسبقًا.');
    });
  });

  describe('تعديل باركود وحدة - startEditUnitBarcode / cancelEditUnitBarcode / saveUnitBarcode', () => {
    it('startEditUnitBarcode يعبّي الحقول من الوحدة', () => {
      component.startEditUnitBarcode({ id: 'u1', unitName: 'كيلو', conversionFactorToBase: 1, isBaseUnit: true, barcodeValue: '123' });

      expect(component.editingUnitBarcodeId).toBe('u1');
      expect(component.editingUnitBarcodeValue).toBe('123');
    });

    it('startEditUnitBarcode يستخدم نص فاضٍ لباركود null', () => {
      component.startEditUnitBarcode({ id: 'u1', unitName: 'كيلو', conversionFactorToBase: 1, isBaseUnit: true, barcodeValue: null });
      expect(component.editingUnitBarcodeValue).toBe('');
    });

    it('cancelEditUnitBarcode يصفّر الحقول', () => {
      component.startEditUnitBarcode({ id: 'u1', unitName: 'كيلو', conversionFactorToBase: 1, isBaseUnit: true, barcodeValue: '123' });
      component.cancelEditUnitBarcode();
      expect(component.editingUnitBarcodeId).toBe('');
    });

    it('saveUnitBarcode يحفظ الباركود الجديد بنجاح', async () => {
      component.editingProductId = 'p1';
      component.editingUnitBarcodeValue = '999';
      apiClientSpy.put.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of([sampleUnit]));

      await component.saveUnitBarcode({ id: 'u1', unitName: 'كيلو', conversionFactorToBase: 1, isBaseUnit: true, barcodeValue: null });

      expect(component.editingUnitBarcodeId).toBe('');
    });

    it('saveUnitBarcode يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      apiClientSpy.put.and.returnValue(throwError(() => ({ error: { detail: 'الباركود مستخدم بوحدة أخرى.' } })));

      await component.saveUnitBarcode({ id: 'u1', unitName: 'كيلو', conversionFactorToBase: 1, isBaseUnit: true, barcodeValue: null });

      expect(component.unitBarcodeError()).toBe('الباركود مستخدم بوحدة أخرى.');
    });
  });

  describe('إدارة الفروع - availableBranchesToAdd / openAddBranchForm / submitNewBranch / toggleBranchAvailability', () => {
    it('availableBranchesToAdd يستثني الفروع المربوطة أصلًا بالمنتج', () => {
      component.branches.set([{ id: 'b1', name: 'الرئيسي' }, { id: 'b2', name: 'الفرعي' }]);
      component.productBranches.set([sampleProductBranch]);

      const available = component.availableBranchesToAdd;

      expect(available.length).toBe(1);
      expect(available[0].id).toBe('b2');
    });

    it('openAddBranchForm يختار أول فرع متاح للإضافة', () => {
      component.branches.set([{ id: 'b2', name: 'الفرعي' }]);
      component.productBranches.set([]);

      component.openAddBranchForm();

      expect(component.newBranchId).toBe('b2');
      expect(component.addBranchFormOpen()).toBeTrue();
    });

    it('submitNewBranch يرفض بلا فرع أو سعر صالح', async () => {
      component.newBranchId = '';
      component.newBranchPrice = null;

      await component.submitNewBranch();

      expect(component.addBranchError()).toBe('اختر فرع وسعر بيع صالح (صفر أو أكتر).');
      expect(apiClientSpy.post).not.toHaveBeenCalled();
    });

    it('submitNewBranch يضيف الفرع بنجاح', async () => {
      component.newBranchId = 'b2';
      component.newBranchPrice = 5;
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of([sampleProductBranch]));

      await component.submitNewBranch();

      expect(component.addBranchFormOpen()).toBeFalse();
    });

    it('submitNewBranch يعرض رسالة الخطأ التفصيلية من الباك إند لو موجودة', async () => {
      component.newBranchId = 'b2';
      component.newBranchPrice = 5;
      apiClientSpy.post.and.returnValue(throwError(() => ({ error: { detail: 'الفرع مربوط أصلًا.' } })));

      await component.submitNewBranch();

      expect(component.addBranchError()).toBe('الفرع مربوط أصلًا.');
    });

    it('toggleBranchAvailability يرسل عكس الحالة الحالية', async () => {
      apiClientSpy.post.and.returnValue(of({}));
      apiClientSpy.get.and.returnValue(of([sampleProductBranch]));

      await component.toggleBranchAvailability(sampleProductBranch);

      expect(apiClientSpy.post).toHaveBeenCalledWith(
        jasmine.anything(),
        jasmine.anything(),
        { isAvailableForSale: false },
        jasmine.objectContaining({ productBranchId: 'pb1' })
      );
      expect(component.togglingBranchAvailabilityId()).toBeNull();
    });

    it('toggleBranchAvailability يعرض رسالة خطأ عربية واضحة عند الفشل', async () => {
      apiClientSpy.post.and.returnValue(throwError(() => new Error('network')));

      await component.toggleBranchAvailability(sampleProductBranch);

      expect(component.addBranchError()).toBe('تعذّر تغيير حالة توفّر المنتج بهذا الفرع.');
    });
  });

  describe('تعديل سعر البيع - startEditBranchPrice / cancelEditBranchPrice / saveBranchPrice', () => {
    it('startEditBranchPrice يعبّي السعر الحالي', () => {
      component.startEditBranchPrice(sampleProductBranch);
      expect(component.editingBranchPriceId).toBe('pb1');
      expect(component.editingBranchPriceValue).toBe(5);
    });

    it('cancelEditBranchPrice يصفّر الحقول', () => {
      component.startEditBranchPrice(sampleProductBranch);
      component.cancelEditBranchPrice();
      expect(component.editingBranchPriceId).toBe('');
      expect(component.editingBranchPriceValue).toBeNull();
    });

    it('saveBranchPrice يرفض سعرًا سالبًا أو فاضيًا', async () => {
      component.editingBranchPriceValue = -1;

      await component.saveBranchPrice(sampleProductBranch);

      expect(component.branchPriceError()).toBe('السعر يجب أن يكون رقمًا موجبًا أو صفرًا.');
    });

    it('saveBranchPrice يعرض "تم تعديل السعر فورًا" لو applied=true (المستوى المباشر)', async () => {
      component.startEditBranchPrice(sampleProductBranch);
      component.editingBranchPriceValue = 6;
      apiClientSpy.post.and.returnValue(of({ applied: true }));
      apiClientSpy.get.and.returnValue(of([sampleProductBranch]));

      await component.saveBranchPrice(sampleProductBranch);

      expect(component.branchPriceMessage()).toBe('تم تعديل السعر فورًا.');
    });

    it('saveBranchPrice يعرض رسالة "بانتظار موافقة إدارية" لو applied=false (سماح مع مراجعة)', async () => {
      component.startEditBranchPrice(sampleProductBranch);
      component.editingBranchPriceValue = 50;
      apiClientSpy.post.and.returnValue(of({ applied: false }));
      apiClientSpy.get.and.returnValue(of([sampleProductBranch]));

      await component.saveBranchPrice(sampleProductBranch);

      expect(component.branchPriceMessage()).toBe('أُرسل طلب تعديل السعر - بانتظار موافقة إدارية قبل ما يصير فعليًا.');
    });

    it('saveBranchPrice يعرض رسالة خطأ عربية (مثل منع مطلق للصلاحية) عند الفشل', async () => {
      component.editingBranchPriceValue = 999;
      apiClientSpy.post.and.returnValue(throwError(() => new Error('403')));

      await component.saveBranchPrice(sampleProductBranch);

      expect(component.branchPriceError()).toBe('تعذّر إرسال تعديل السعر - قد لا تملك الصلاحية اللازمة.');
    });
  });
});
