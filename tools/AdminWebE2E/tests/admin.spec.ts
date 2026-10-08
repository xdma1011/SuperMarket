import { test, expect } from '@playwright/test';
import { AdminApi, loginUi, num, rnd } from './helpers';

/**
 * كل اختبار: تحضير عبر الـAPI (بيانات جديدة بأسماء فريدة)، الفعل نفسه من الشاشة (زي صاحب المحل)، وبعدين مقارنة
 * اللي انعرض بالشاشة مع اللي انحفظ بالسيرفر. الهدف: نماذج بتبعت أرقام غلط - اختبارات الـAPI ما بتمسكها.
 */

test.describe.configure({ mode: 'serial' });

let api: AdminApi;
test.beforeAll(async () => { api = await AdminApi.create(); });

test('فاتورة شراء: نفس الصنف بالحبة والكرتونة + صنف دفعات، الإجمالي والمخزون مطابقين، ثم دفعة مورد', async ({ page }) => {
  const tag = rnd();
  const pieceName = `حليب E2E ${tag}`;
  const batchName = `لبن دفعات E2E ${tag}`;
  const piece = await api.createProduct(pieceName, 1.25, [['حبة', 1], ['كرتونة', 12]]);
  const batch = await api.createProduct(batchName, 1.9, [['علبة', 1]], true);
  const supplierName = `مورد E2E ${tag}`;
  const supplierId = await api.createSupplier(supplierName);
  const cartonUnit = piece.units.find((u: any) => Number(u.conversionFactorToBase) === 12);

  const errors = await loginUi(page);
  await page.goto('/purchases');
  await page.getByRole('button', { name: 'فاتورة شراء جديدة' }).click();
  const modal = page.locator('.modal-card');
  await modal.locator('label.field', { hasText: 'المورد' }).locator('select').selectOption(supplierId);
  await modal.locator('label.field', { hasText: 'الفرع' }).locator('select').selectOption(api.branchId);
  await modal.locator('label.field', { hasText: 'مرجع فاتورة المورد' }).locator('input').fill(`E2E-${tag}`);

  const addLine = async (productId: string) => {
    await modal.locator('select.line-select').selectOption(productId);
    await modal.getByRole('button', { name: 'إضافة صنف' }).click();
  };
  await addLine(piece.id);
  await addLine(piece.id);           // نفس الصنف مرة تانية (سطر كرتونة)
  await addLine(batch.id);

  const rows = modal.locator('.lines-row:has(select.line-unit-select)');
  await expect(rows, 'لازم 3 أسطر: حبة + كرتونة + دفعات').toHaveCount(3);
  await rows.nth(0).locator('input[type=number]').nth(0).fill('5');
  await rows.nth(0).locator('input[type=number]').nth(1).fill('0.8');
  await rows.nth(1).locator('select.line-unit-select').selectOption(cartonUnit.id);
  await rows.nth(1).locator('input[type=number]').nth(0).fill('2');
  await rows.nth(1).locator('input[type=number]').nth(1).fill('9');
  await rows.nth(2).locator('input[type=number]').nth(0).fill('10');
  await rows.nth(2).locator('input[type=number]').nth(1).fill('1.3');
  await modal.locator('label.field', { hasText: 'رقم الدفعة' }).locator('input').fill(`B-${tag}`);

  // بعد التعبئة: كل سطر لازم يضل بوحدته (سطر الكرتونة ما يرجع حبة).
  await expect(rows.nth(0).locator('select.line-unit-select')).toHaveValue(piece.units.find((u: any) => u.isBaseUnit).id);
  await expect(rows.nth(1).locator('select.line-unit-select')).toHaveValue(cartonUnit.id);
  await expect(modal.locator('.lines-total .mono')).toHaveText('35.000');

  await modal.getByRole('button', { name: 'تسجيل الفاتورة' }).click();
  await expect(modal).toBeHidden();

  const list = await api.get(`/purchase-invoices?branchId=${api.branchId}&pageSize=20`);
  const invoice = (list.items as any[]).find(i => i.supplierInvoiceReference === `E2E-${tag}`);
  expect(invoice, 'الفاتورة انحفظت').toBeTruthy();
  expect(Number(invoice.totalAmount)).toBeCloseTo(35, 3);
  expect(await api.stockOf(piece.id, pieceName), 'مخزون الحليب = 5 حبات + 2 كرتونة × 12').toBe(29);
  expect(await api.stockOf(batch.id, batchName)).toBe(10);

  const row = page.locator('tr', { hasText: invoice.invoiceNumber });
  await expect(row.locator('td').nth(3)).toHaveText('35.000');
  await expect(row.locator('.debt-chip')).toHaveText('35.000');

  // دفعة مورد من الشاشة: 10.250 ← المتبقي 24.750 بالشاشة وبالسيرفر.
  await row.getByRole('button', { name: 'تسجيل دفعة' }).click();
  const pay = page.locator('.modal-card');
  await pay.locator('label.field', { hasText: 'المبلغ' }).locator('input').fill('10.25');
  await pay.locator('label.field', { hasText: 'طريقة الدفع' }).locator('select').selectOption({ index: 0 });
  await pay.getByRole('button', { name: 'تسجيل الدفعة' }).click();
  await expect(pay).toBeHidden();
  await expect(row.locator('.debt-chip')).toHaveText('24.750');
  const after = (await api.get(`/purchase-invoices?branchId=${api.branchId}&pageSize=20`)).items.find((i: any) => i.id === invoice.id);
  expect(Number(after.totalAmount) - Number(after.totalPaidAmount)).toBeCloseTo(24.75, 3);

  expect(errors.filter(e => /NG0|Error/.test(e)), 'أخطاء Angular بالكونسول').toEqual([]);
});

test('مصروف من صفحة المالية: المبلغ بـ3 خانات وصل للسيرفر زي ما انكتب', async ({ page }) => {
  const tag = rnd();
  const now = new Date();
  await loginUi(page);
  await page.goto('/finance');
  await page.getByRole('button', { name: 'المصاريف', exact: true }).click();
  await page.getByRole('button', { name: 'مصروف جديد' }).click();
  const modal = page.locator('.modal-card');
  const selects = modal.locator('select');
  await selects.nth(0).selectOption(api.branchId);
  await selects.nth(1).selectOption({ index: 1 });
  await modal.locator('input[type=number]').first().fill('12.345');
  await modal.locator('input[type=text]').last().fill(`E2E-${tag}`);
  await modal.locator('button.btn-primary').click();
  await expect(modal).toBeHidden();

  const month = now.getMonth() + 1;
  const expenses = await api.get(`/finance/expenses?branchId=${api.branchId}&pageSize=50`);
  const saved = (expenses.items as any[]).find(e => e.notes === `E2E-${tag}`);
  expect(saved, 'المصروف انحفظ').toBeTruthy();
  expect(Number(saved.amount)).toBe(12.345);
  expect(saved.periodMonth).toBe(month);
  await expect(page.locator('tr', { hasText: `E2E-${tag}` })).toContainText('12.345');
});

test('تقفيل بفرق: "تفسير الفرق" من الشاشة بيصفّر غير المفسَّر بالسيرفر', async ({ page }) => {
  const day = 1 + Math.floor(Math.random() * 27);
  const businessDate = `2025-0${1 + Math.floor(Math.random() * 9)}-${String(day).padStart(2, '0')}`;
  const closing = await api.post('/cash-closings', { branchId: api.branchId, businessDate, countedCash: 7.5, countedDetails: [], shiftNumber: null });
  expect(Number(closing.variance)).not.toBe(0);

  await loginUi(page);
  await page.goto('/cash-closings');
  const row = page.locator('tr', { hasText: businessDate }).filter({ has: page.getByRole('button', { name: 'تفسير الفرق' }) }).last();
  await row.getByRole('button', { name: 'تفسير الفرق' }).click();
  const box = page.locator('.explain-box');
  await box.getByRole('button', { name: 'كل المتبقي' }).click();
  await box.locator('input[type=text]').fill('E2E تفسير');
  await box.getByRole('button', { name: 'حفظ التفسير' }).click();
  await expect(box.locator('.explain-note')).toContainText('E2E تفسير');

  const notes = await api.get(`/cash-closings/${closing.cashClosingId}/variance-notes`);
  expect(Number(notes.explainedTotal)).toBeCloseTo(Math.abs(Number(closing.variance)), 3);
  await page.reload();
  await expect(page.locator('tr', { hasText: businessDate }).last()).toContainText('مفسَّر');
});

test('كشف شريك: مصروف بفترة كشف نازل بيعلّمه "قديم" بالشاشة', async ({ page }) => {
  const tag = rnd();
  await api.post('/partners', { branchId: api.branchId, fullName: `شريك E2E ${tag}`, type: 'Speculative', userId: null, speculativeProfitPercent: 10, notes: null });
  const now = new Date();
  const prev = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  await api.post('/partners/statements', { branchId: api.branchId, year: prev.getFullYear(), month: prev.getMonth() + 1 });
  const types = await api.get('/finance/expense-types');
  const typeId = (types.items ?? types)[0].id;
  await api.post('/finance/expenses', {
    branchId: api.branchId, category: null, amount: 1.5, paymentDateUtc: new Date().toISOString(),
    periodYear: prev.getFullYear(), periodMonth: prev.getMonth() + 1, notes: `E2E stale ${tag}`, expenseTypeId: typeId, paidFromDrawer: false
  });

  const statements: any[] = await api.get(`/partners/statements?branchId=${api.branchId}`);
  const st = (statements as any).items ? (statements as any).items : statements;
  const target = st.find((s: any) => s.year === prev.getFullYear() && s.month === prev.getMonth() + 1);
  expect(target?.staleSinceUtc, 'السيرفر علّم الكشف قديم').toBeTruthy();

  await loginUi(page);
  await page.goto('/partners');
  await page.getByRole('button', { name: 'الكشوف الشهرية' }).click();
  await expect(page.locator('tr', { hasText: `${prev.getMonth() + 1}/${prev.getFullYear()}` })).toContainText('قديم');
});

test('بيعة مرفوضة من الكاشير بتظهر بصفحة "بيعات مرفوضة" بنفس المبلغ', async ({ page }) => {
  const tag = rnd();
  const name = `صنف رفض E2E ${tag}`;
  const product = await api.createProduct(name, 2, [['حبة', 1]]);
  const clientRequestId = crypto.randomUUID();
  const r = await api.postRaw('/sales', {
    branchId: api.branchId, clientRequestId, customerId: null, invoiceLevelDiscountAmount: 0,
    items: [{ productId: product.id, productUnitId: product.units[0].id, quantity: 1, manualDiscountAmount: 0, productBatchId: null, catalogVersion: 0 }],
    payments: [{ paymentMethodId: (await api.get('/payment-methods'))[0].id, amount: 99.999, externalReference: null, clientRequestId: crypto.randomUUID() }]
  }, { 'X-Client-App': 'Cashier' });
  expect(r.status(), await r.text()).toBeGreaterThanOrEqual(400);

  await loginUi(page);
  await page.goto('/rejected-sales');
  await expect(page.locator('body')).toContainText('99.999');
});

test('فاتورة شراء: حذف سطر من سطرين لنفس الصنف بيشيل السطر الصح', async ({ page }) => {
  const tag = rnd();
  const product = await api.createProduct(`صنف سطرين E2E ${tag}`, 1, [['حبة', 1], ['كرتونة', 6]]);
  const carton = product.units.find((u: any) => Number(u.conversionFactorToBase) === 6);
  const warnings: string[] = [];
  page.on('console', m => { if (m.type() === 'warning' || m.type() === 'error') warnings.push(m.text()); });
  await loginUi(page);
  await page.goto('/purchases');
  await page.getByRole('button', { name: 'فاتورة شراء جديدة' }).click();
  const modal = page.locator('.modal-card');
  for (let i = 0; i < 2; i++) {
    await modal.locator('select.line-select').selectOption(product.id);
    await modal.getByRole('button', { name: 'إضافة صنف' }).click();
  }
  const rows = modal.locator('.lines-row:has(select.line-unit-select)');
  await expect(rows).toHaveCount(2);
  await rows.nth(0).locator('input[type=number]').nth(0).fill('3');
  await rows.nth(1).locator('select.line-unit-select').selectOption(carton.id);
  await rows.nth(1).locator('input[type=number]').nth(0).fill('7');
  // شيل السطر الأول (الحبة) - لازم يضل سطر الكرتونة بكميته 7.
  await rows.nth(0).getByRole('button', { name: 'حذف السطر' }).click();
  await expect(rows).toHaveCount(1);
  await expect(rows.nth(0).locator('select.line-unit-select')).toHaveValue(carton.id);
  await expect(rows.nth(0).locator('input[type=number]').nth(0)).toHaveValue('7');
  expect(warnings.filter(w => /NG0955|duplicate/i.test(w)), 'تحذير مفاتيح مكررة بـ@for').toEqual([]);
});

test('جرد جزئي من الشاشة: عدّ بالكتابة حرف حرف، إكمال، اعتماد - المخزون = المعدود', async ({ page }) => {
  const tag = rnd();
  const name = `صنف جرد E2E ${tag}`;
  const product = await api.createProduct(name, 1, [['حبة', 1]]);
  const supplierId = await api.createSupplier(`مورد جرد ${tag}`);
  await api.post('/purchase-invoices', {
    branchId: api.branchId, supplierId, supplierInvoiceReference: `ST-${tag}`, imageReferences: null, dueDate: null,
    items: [{ productId: product.id, productUnitId: product.units[0].id, quantity: 30, unitCost: 0.5, existingProductBatchId: null, newBatchNumber: null, newBatchExpiryDate: null }]
  });

  await loginUi(page);
  await page.goto('/stocktakes');
  await page.getByRole('button', { name: 'جرد جديد' }).click();
  const modal = page.locator('.modal-card');
  await modal.locator('select').first().selectOption(api.branchId);
  await modal.locator('input[name="scope"]').nth(1).check();
  await modal.locator('input[placeholder^="دوّر على مادة"]').fill(name);
  await modal.getByRole('button', { name }).click();
  await modal.locator('button.btn-primary').click();
  await expect(page).toHaveURL(/\/stocktakes\/[0-9a-f-]{36}/);

  const input = page.locator('tr', { hasText: name }).locator('input.count-input');
  await input.click();
  await input.pressSequentially('27', { delay: 60 });   // زي إنسان - كل حرف بيحفظ
  await input.press('Tab');               // الخانة بتتأكد بالخروج منها (أو Enter)
  await page.waitForTimeout(1500);
  await expect(input, 'المعدود لازم يضل 27 (مش 2)').toHaveValue('27');
  const stocktakeId = page.url().split('/').pop()!;
  const detail = await api.get(`/stocktakes/${stocktakeId}`);
  const item = (detail.items as any[]).find(i => i.productId === product.id);
  expect(Number(item.countedQuantity), 'المعدود المحفوظ بالسيرفر').toBe(27);

  await page.getByRole('button', { name: 'إكمال العدّ' }).click();
  await page.getByRole('button', { name: 'اعتماد الجرد' }).click();
  await expect(page.locator('body')).toContainText('-3');
  await expect.poll(() => api.stockOf(product.id, name)).toBe(27);
});

test('موظف جديد براتب بـ3 خانات وصرف راتبه من الشاشة', async ({ page }) => {
  const tag = rnd();
  const name = `موظف E2E ${tag}`;
  await loginUi(page);
  await page.goto('/employees');
  await page.getByRole('button', { name: '+ موظف جديد' }).click();
  const modal = page.locator('.modal-card');
  await modal.locator('label.field', { hasText: 'الاسم' }).locator('input').fill(name);
  await modal.locator('label.field', { hasText: 'الراتب الشهري' }).locator('input').fill('350.125');
  await modal.getByRole('button', { name: 'حفظ' }).click();
  await expect(modal).toBeHidden();
  const row = page.locator('tr', { hasText: name });
  await expect(row).toContainText('350.125');

  await row.getByRole('button', { name: 'صرف راتب' }).click();
  const pay = page.locator('.modal-card');
  await pay.locator('input[type=number]').first().fill('350.125');
  await pay.getByRole('button', { name: 'صرف' }).click();
  await expect(pay).toBeHidden();

  const employees = await api.get(`/employees?branchId=${api.branchId}`);
  const e = ((employees as any).items ?? employees).find((x: any) => x.fullName === name);
  expect(Number(e.monthlySalary)).toBe(350.125);
  expect(Number(e.totalSalariesPaid)).toBe(350.125);
});
