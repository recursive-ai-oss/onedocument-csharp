# Registers the on-demand task that runs Run-Oracle.ps1 in the signed-in user's desktop session.
# "Interactive" logon type means the task runs only while the user is logged on, which Office automation needs.
# Everything is placed under <profile>\oneoffice; nothing else on the machine is changed.

param([string]$Base = (Join-Path $env:USERPROFILE 'oneoffice'))

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Join-Path $Base 'driver'), (Join-Path $Base 'jobs') | Out-Null

$script = Join-Path $Base 'driver\Run-Oracle.ps1'
if (-not (Test-Path $script)) { throw "missing $script" }

$name = 'OneOfficeOracle'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $script + '" -Base "' + $Base + '"')
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 45) -StartWhenAvailable
Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Get-ScheduledTask -TaskName $name | Select-Object TaskName, State | Format-Table -AutoSize | Out-String
