[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
 [ValidateSet('Install','Status','Uninstall')][string]$Action='Status',
 [string]$ProductRoot,
 [string]$TaskName='RecepcionDocumental-GmailSync',
 [System.Management.Automation.PSCredential]$Credential,
 [switch]$Interactive
)
$ErrorActionPreference='Stop'
if($Action -eq 'Status'){Get-ScheduledTask -TaskName $TaskName;Get-ScheduledTaskInfo -TaskName $TaskName;return}
if($Action -eq 'Uninstall'){if($PSCmdlet.ShouldProcess($TaskName,'Uninstall')){Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false};return}
$root=(Resolve-Path -LiteralPath $ProductRoot).Path
$exe=Join-Path $root 'tools\RecepcionDocumental.SyncRunner\bin\RecepcionDocumental.SyncRunner.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build/deploy SyncRunner first.'}
if(Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue){throw 'Task already exists; inspect it before replacing.'}
if($Interactive -and $Credential){throw 'Interactive development mode uses the current user; do not supply credentials.'}
if($PSCmdlet.ShouldProcess($TaskName,'Install five-minute Gmail synchronization')){
 $taskAction=New-ScheduledTaskAction -Execute $exe -Argument ('"'+$root+'"') -WorkingDirectory $root
 $delay=1;if($Interactive){$delay=5}
 $trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes($delay) -RepetitionInterval (New-TimeSpan -Minutes 5)
 $settings=New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
 if($Interactive){
  $user=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
  $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
  Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Trigger $trigger -Settings $settings -Principal $principal -Description 'DEV: current user must be logged on; no password. One-shot net48/x64 Gmail receiver; SQL session applock is authoritative.'
 }else{
  if(!$Credential){$Credential=Get-Credential -Message 'Windows account authorized for SQL, MachineKey, OAuth environment and operational directories'}
  Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Trigger $trigger -Settings $settings -User $Credential.UserName -Password $Credential.GetNetworkCredential().Password -Description 'One-shot net48/x64 Gmail receiver; SQL session applock is authoritative.'
 }
}
