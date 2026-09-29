/** ApiController.Employees - الموظفين والرواتب والسلف (Finance.Manage)، راجع EmployeeEndpoints.cs. */
export enum EmployeesOperation {
  List = '',
  Create = '',
  Update = '{id}',
  Payments = 'payments',
  RecordPayment = '{id}/payments'
}
