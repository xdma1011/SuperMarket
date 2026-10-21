/** ApiController.Partners - وحدة الشركاء (Partners.Manage)، راجع PartnersEndpoints.cs. */
export enum PartnersOperation {
  List = '',
  Create = '',
  Update = '{id}',
  SetActive = '{id}/active',
  TelegramPhone = '{id}/telegram-phone',
  CashierBarcode = '{id}/cashier-barcode',
  Ledger = '{id}/ledger',
  ReinvestPreview = '{id}/reinvest-preview',
  Reinvest = '{id}/reinvest',
  Reinvestments = '{id}/reinvestments',
  ReverseReinvestmentPreview = 'reinvestments/{id}/reverse-preview',
  ReverseReinvestment = 'reinvestments/{id}/reverse',
  Withdrawals = 'withdrawals',
  RecordWithdrawal = 'withdrawals',
  Statements = 'statements',
  StatementById = 'statements/{id}',
  GenerateStatement = 'statements',
  OwnerReceivables = 'owner-receivables',
  OwnerRepayment = 'owner-receivables/repayments'
}
