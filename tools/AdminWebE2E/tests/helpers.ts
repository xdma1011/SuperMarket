import { APIRequestContext, Page, expect, request } from '@playwright/test';

export const API = (process.env.E2E_API ?? 'http://localhost:5000/api/v1').replace(/\/$/, '');
export const ADMIN_USER = process.env.E2E_ADMIN_USER ?? 'admin';
export const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? '123';

export const rnd = () => Math.floor(10000 + Math.random() * 89999).toString();

/** عميل API بتوكن الأدمن - للتحضير وللتحقق إن الأرقام المعروضة = الأرقام بالسيرفر. */
export class AdminApi {
  private constructor(private readonly ctx: APIRequestContext, public readonly branchId: string) {}

  static async create(): Promise<AdminApi> {
    const anon = await request.newContext();
    const login = await anon.post(`${API}/auth/login`, { data: { username: ADMIN_USER, password: ADMIN_PASSWORD, appType: 'Cashier' } });
    // appType Cashier عمدًا: دخول الشاشة (Admin) بيسحب جلسة الـAdmin التانية لنفس المستخدم (جلسة وحدة لكل تطبيق).
    expect(login.ok(), `دخول الأدمن عبر الـAPI: ${login.status()} ${await login.text()}`).toBeTruthy();
    const body = await login.json();
    const ctx = await request.newContext({ extraHTTPHeaders: { Authorization: `Bearer ${body.accessToken}` } });
    let branchId: string = body.branchId;
    if (!branchId) {
      branchId = (await (await ctx.get(`${API}/branches?pageSize=10`)).json()).items[0].id;
    }
    return new AdminApi(ctx, branchId);
  }

  async get<T = any>(path: string): Promise<T> {
    const r = await this.ctx.get(`${API}${path}`);
    expect(r.ok(), `GET ${path}: ${r.status()} ${await r.text()}`).toBeTruthy();
    return r.json();
  }

  async post<T = any>(path: string, data: unknown): Promise<T> {
    const r = await this.ctx.post(`${API}${path}`, { data });
    expect(r.ok(), `POST ${path}: ${r.status()} ${await r.text()}`).toBeTruthy();
    const text = await r.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  async postRaw(path: string, data: unknown, headers?: Record<string, string>) {
    return this.ctx.post(`${API}${path}`, { data, headers });
  }

  /** منتج بفرع وسعر (§1.8) - وحدات [اسم، معامل]. */
  async createProduct(name: string, price: number, units: [string, number][], isBatchTracked = false) {
    const category = await this.post('/product-categories', { name: `تصنيف ${name}`, parentCategoryId: null });
    const created = await this.post('/products', {
      name, description: null, categoryId: category.categoryId, isBatchTracked,
      suggestedRetailPrice: null, expectedShelfLifeDays: null,
      units: units.map(([unitName, f]) => ({ unitName, conversionFactorToBase: f, isBaseUnit: f === 1 })),
      barcodes: units.map(([unitName], i) => ({ barcodeValue: `88${rnd()}${i}${Date.now() % 100000}`, unitName }))
    });
    await this.post(`/products/${created.productId}/branches`, { branchId: this.branchId, sellingPrice: price, minimumStock: 0, maximumStock: null });
    const unitRows: any[] = await this.get(`/products/${created.productId}/units`);
    return { id: created.productId as string, units: unitRows };
  }

  async createSupplier(name: string) {
    const s = await this.post('/suppliers', { name, contactName: null, phone: `079${rnd()}11`, email: null, street: null, city: 'عمّان', postalCode: null, country: 'الأردن' });
    return s.supplierId as string;
  }

  async stockOf(productId: string, productName: string): Promise<number> {
    const page = await this.get(`/inventory/current-stock?branchId=${this.branchId}&pageSize=50&search=${encodeURIComponent(productName)}`);
    const items: any[] = page.items ?? page;
    return items.filter(i => i.productId === productId).reduce((s, i) => s + Number(i.quantityOnHand ?? i.quantity ?? 0), 0);
  }
}

/** دخول من شاشة الدخول نفسها (التوكن بالذاكرة، فكل اختبار بيدخل). */
export async function loginUi(page: Page) {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
  page.on('console', m => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
  await page.goto('/login');
  await page.locator('input[name="username"]').fill(ADMIN_USER);
  await page.locator('input[name="password"]').fill(ADMIN_PASSWORD);
  await page.locator('button[type="submit"]').click();
  await expect(page).not.toHaveURL(/\/login/);
  return errors;
}

/** "1,234.500" → 1234.5 */
export const num = (text: string | null) => Number((text ?? '').replace(/[^\d.\-]/g, ''));
