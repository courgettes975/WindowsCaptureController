<#
.SYNOPSIS
  Builds docs\icons-preview.html: every SVG source rendered like a Keypad key (dark background, label),
  at the LCD key size (116 px) and a small size (60 px), with the same token substitution as the plugin.
  Used to review legibility before checking on the real device.
#>
param([string]$IconDir = (Join-Path $PSScriptRoot '..\assets\icons'),
      [string]$Out = (Join-Path $PSScriptRoot '..\docs\icons-preview.html'))
$ErrorActionPreference = 'Stop'
function Fill([string]$svg, [string]$fg, [string]$accent, [string]$bg, [string]$sw) {
  $svg.Replace('ACCENT', $accent).Replace('FG', $fg).Replace('BG', $bg).Replace("'SW'", "'$sw'")
}
$tiles = foreach ($f in Get-ChildItem $IconDir -Filter *.svg | Sort-Object Name) {
  $src = Get-Content $f.FullName -Raw
  if ($f.BaseName -eq 'Empty') { continue }
  $big = Fill $src '#FFFFFF' '#E81123' '#121418' '10'
  $act = Fill $src '#FFFFFF' '#0067C0' '#0067C0' '25'
  $dim = Fill $src '#5F646E' '#5F646E' '#121418' '10'
  @"
<figure><div class="key">$big<span>$($f.BaseName)</span></div>
<div class="row"><div class="key s">$act</div><div class="key s dim">$dim</div></div></figure>
"@
}
$html = @"
<!doctype html><html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Icônes LCD</title>
<style>
 :root{--bg:#1b1d22;--fg:#e8eaed;--key:#121418}
 @media (prefers-color-scheme: light){:root:not([data-theme="dark"]){--bg:#f2f3f5;--fg:#1b1d22}}
 body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px system-ui,Segoe UI,sans-serif}
 h1{font-size:18px;margin:0 0 4px} p{margin:0 0 16px;opacity:.75}
 .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(132px,1fr));gap:14px}
 figure{margin:0}
 .key{width:116px;height:116px;background:var(--key);border-radius:12px;position:relative;display:flex;align-items:flex-start;justify-content:center}
 .key svg{width:66px;height:66px;margin-top:12px}
 .key span{position:absolute;bottom:8px;left:0;right:0;text-align:center;color:#ebeef2;font-size:13px}
 .row{display:flex;gap:6px;margin-top:6px} .key.s{width:55px;height:55px;border-radius:8px} .key.s svg{width:36px;height:36px;margin-top:9px}
 .key.s:first-child{outline:3px solid #0067C0}
</style></head><body>
<h1>Icônes LCD — Windows Capture Controller</h1>
<p>Rendu des sources SVG (assets/icons) avec les mêmes couleurs que le plugin : normal (blanc, accent rouge), actif (cadre bleu), indisponible (gris). Taille d'une touche du MX Creative Keypad simulée à 116 px.</p>
<div class="grid">
$($tiles -join "`n")
</div></body></html>
"@
Set-Content -Path $Out -Value $html -Encoding utf8
"Preview written: $Out ($(@($tiles).Count) icons)"
