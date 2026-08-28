/** ApiController.Auth */
export enum AuthOperation {
  Login = 'login',
  Refresh = 'refresh',
  Logout = 'logout',
  PublicBranches = 'branches',
  MyPermissions = 'my-permissions'
}

/** ApiController.AuthSessions */
export enum AuthSessionsOperation {
  List = '',
  Revoke = '{id}/revoke'
}
