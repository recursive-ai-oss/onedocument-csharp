# Registers a one-off task for a single run and starts it, so every run carries its own job directory and two runs
# can never overwrite each other's pointer. The runs then queue on the shared lock inside Run-Oracle.ps1.
# The task is registered like Install.ps1 does (signed-in user's desktop session) and removed by the caller afterwards.

param([Parameter(Mandatory)][string]$Base, [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+$')][string]$Run)

$ErrorActionPreference = 'Stop'
$script = Join-Path $Base 'driver\Run-Oracle.ps1'
$job = Join-Path $Base ('jobs\' + $Run)
if (-not (Test-Path $script)) { throw "missing $script" }
if (-not (Test-Path (Join-Path $job 'job.json'))) { throw "missing job.json in $job" }
$name = 'OneOfficeOracle-' + $Run
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $script + '" -Base "' + $Base + '" -Job "' + $job + '"')
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 60) -StartWhenAvailable
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $name
