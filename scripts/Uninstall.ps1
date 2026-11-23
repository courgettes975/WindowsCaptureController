<#
.SYNOPSIS
  Uninstalls Windows Capture Controller. Removes ONLY its own files.
.PARAMETER Complete
  Also delete configuration and logs (%LOCALAPPDATA%\WindowsCaptureController). Asks for confirmation.
.NOTES
  Never touches Logitech profiles nor the Windows Snipping Tool. Never closes Logi Options+.
  Options: default = uninstall and keep settings; -Complete = full removal.
#>
param([switch]$Complete)
$ErrorActionPreference = 'Stop'
$name = 'WindowsCaptureController'
$pluginsDir = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'

# Stop our own agent only (by exact name).
Get-Process WccAgent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$link = Join-Path $pluginsDir "$name.link"
if (Test-Path $link) { Remove-Item $link -Force; Write-Host "Lien développeur supprimé." }

if (Test-Path (Join-Path $pluginsDir $name)) {
    if (Get-Command logiplugintool -ErrorAction SilentlyContinue) {
        logiplugintool uninstall $name
        if ($LASTEXITCODE -ne 0) { Write-Warning "logiplugintool uninstall a échoué ($LASTEXITCODE)." }
    } else {
        Write-Warning "logiplugintool absent : supprimez le plugin depuis Logi Options+ (Marketplace > Installés)."
    }
}
Start-Process "loupedeck:plugin/$name/reload" -ErrorAction SilentlyContinue

$data = Join-Path $env:LOCALAPPDATA $name
if ($Complete -and (Test-Path $data)) {
    $answer = Read-Host "Supprimer la configuration et les journaux ($data) ? [o/N]"
    if ($answer -match '^(o|oui|y|yes)$') { Remove-Item $data -Recurse -Force; Write-Host "Configuration et journaux supprimés." }
    else { Write-Host "Configuration et journaux conservés." }
} elseif (Test-Path $data) {
    Write-Host "Configuration et journaux conservés : $data (utilisez -Complete pour les supprimer)."
}
Write-Host "Désinstallation terminée. L'Outil Capture de Windows n'a pas été modifié."
