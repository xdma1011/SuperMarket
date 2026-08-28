/** ApiController.Backups */
export enum BackupsOperation {
  Trigger = '',
  TriggerAndDownload = 'download',
  List = '',
  Download = '{id}/download',
  Delete = '{id}'
}
