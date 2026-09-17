/** ApiController.Finance - Finance.Manage حصرًا (Master Admin افتراضيًا)، راجع FinanceEndpoints.cs. */
export enum FinanceOperation {
  CreateExpense = 'expenses',
  GetExpenses = 'expenses',
  CreateCapitalTransaction = 'capital-transactions',
  GetCapitalTransactions = 'capital-transactions',
  GetProfitStatement = 'profit-statement'
}
