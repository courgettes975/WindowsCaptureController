<#
.SYNOPSIS
  Installs Windows Capture Controller into Logi Options+.
.PARAMETER Dev
  Developer install: Debug build + .link file in %LOCALAPPDATA%\Logi\LogiPluginService\Plugins
  pointing to bin\Debug (rebuild = hot reload). Otherwise: Release package + logiplugintool install.
.PARAMETER Package
  Install an existing .lplug4 instead of building one.
.NOTES
  Never closes Logi Options+. Only the plugin is (re)loaded through loupedeck:plugin/<name>/reload.
#>
param([switch]$Dev, [string]$Package)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$name = 'WindowsCaptureController'
$pluginsDir = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'
$report = [ordered]@{}
$installStart = Get-Date

# 1. Windows ---------------------------------------------------------------------------------------
$build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
if ($build -lt 22000) { throw "Windows 11 requis (build $build détecté)." }
$report.Windows = "Windows 11 build $build"

# 2. Logi Options+ / Plugin Service ----------------------------------------------------------------
$api = 'C:\Program Files\Logi\LogiPluginService\PluginApi.dll'
if (-not (Test-Path $api)) { throw "Logi Options+ (Logi Plugin Service) n'est pas installé." }
$report.'Logi Plugin Service' = (Get-Item $api).VersionInfo.FileVersion

# 3. MX Creative Keypad (warning only: the plugin installs without the device) ----------------------
$keypad = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like 'USB\VID_046D&PID_C354*' }
$report.'MX Creative Keypad' = if ($keypad) { 'connecté' } else { 'non détecté (le plugin s''installe quand même)' }

# 4. Prerequisites -----------------------------------------------------------------------------------
$snip = Get-AppxPackage -Name Microsoft.ScreenSketch -ErrorAction SilentlyContinue
if (-not $snip) { Write-Warning "Outil Capture d'écran non installé : installez-le depuis le Microsoft Store." }
$report.'Outil Capture' = if ($snip) { $snip.Version } else { 'absent' }
$net8 = (& dotnet --list-runtimes 2>$null) -match 'Microsoft.WindowsDesktop.App 8\.'
if (-not $net8) { Write-Warning ".NET 8 Desktop Runtime introuvable : l'agent WccAgent.exe en a besoin." }

# 5-7. Plugin, icons (in package), configuration (created at first run in phase 4) -------------------
if ($Dev) {
    Write-Host "== Installation développeur (.link)"
    dotnet build (Join-Path $root 'src\WindowsCaptureController.Plugin\WindowsCaptureController.Plugin.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    $report.Mode = 'développeur (.link -> bin\Debug)'
} else {
    if (-not $Package) { $Package = & (Join-Path $PSScriptRoot 'Build-Package.ps1') | Select-Object -Last 1 }
    $link = Join-Path $pluginsDir "$name.link"
    if (Test-Path $link) { Remove-Item $link -Force; Write-Host "Lien développeur supprimé (il masquerait le paquet installé)" }
    Write-Host "== logiplugintool install $Package"
    logiplugintool install $Package
    if ($LASTEXITCODE -ne 0) { throw "Installation refusée ($LASTEXITCODE). Vous pouvez aussi double-cliquer le fichier .lplug4." }
    $report.Mode = "paquet $([IO.Path]::GetFileName($Package))"
}

# 8. Reload only this plugin ---------------------------------------------------------------------------
Start-Process "loupedeck:plugin/$name/reload" -ErrorAction SilentlyContinue

# 9. Verify the load: our own plugin log, lines written AFTER this install started -------------------------
# (the service's per-plugin log can be rotated/recreated at the very moment of a reload)
$ownLog = Join-Path $env:LOCALAPPDATA 'WindowsCaptureController\logs\plugin.log'
function Get-RecentLines {
    if (-not (Test-Path $ownLog)) { return @() }
    Get-Content $ownLog -Tail 200 | Where-Object {
        $_ -match '^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)' -and [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss', $null) -ge $installStart.AddSeconds(-1)
    }
}
$loaded = $false; $agentOk = $false
for ($i = 0; $i -lt 40 -and -not ($loaded -and $agentOk); $i++) {
    Start-Sleep -Milliseconds 500
    $recent = Get-RecentLines
    $loaded = [bool]($recent -match 'Windows Capture Controller .* loaded')
    $agentOk = [bool]($recent -match 'Agent ping OK')
}
$report.'Plugin chargé' = if ($loaded) { 'oui' } else { 'non confirmé (voir le journal du service)' }
$report.'Agent' = if ($agentOk) { 'répond' } else { 'non confirmé' }

# 10. Final report ---------------------------------------------------------------------------------------
Write-Host ""
Write-Host "==== Rapport d'installation ===="
$report.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-22}: {1}" -f $_.Key, $_.Value) }
Write-Host ""
Write-Host "Dans Logi Options+ : MX Creative Keypad > Actions > Windows Capture Controller (groupe « Windows Capture »)."
Write-Host "Journaux : $env:LOCALAPPDATA\WindowsCaptureController\logs"
