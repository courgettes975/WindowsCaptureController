<#
.SYNOPSIS
  (Re)writes the SVG icon sources in assets\icons (one file per Glyph, viewBox 0 0 100 100).
.DESCRIPTION
  Tokens replaced at runtime by the plugin (SvgIcons.cs):  FG = main colour, ACCENT = state / ink colour,
  BG = key background, SW = stroke width (Width glyph only). Simple, high-contrast shapes, readable on the
  Keypad LCD; original drawings (no third-party icon set, no licence issue).
  The SVG files in assets\icons are the source of truth; this script only exists to regenerate them.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets\icons'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$s = 'fill="none" stroke="FG" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"'
$d = 'fill="none" stroke="FG" stroke-width="6" stroke-linecap="round" stroke-dasharray="10 9"'
$icons = [ordered]@{
  Scissors     = "<g $s><line x1='30' y1='72' x2='70' y2='18'/><line x1='70' y1='72' x2='30' y2='18'/><circle cx='26' cy='78' r='11'/><circle cx='74' cy='78' r='11'/></g><circle cx='50' cy='45' r='4' fill='FG'/>"
  NewCapture   = "<rect x='12' y='20' width='76' height='60' rx='4' $d/><g stroke='ACCENT' stroke-width='8' stroke-linecap='round'><line x1='50' y1='36' x2='50' y2='64'/><line x1='36' y1='50' x2='64' y2='50'/></g>"
  Rectangle    = "<rect x='12' y='20' width='76' height='60' rx='4' $d/>"
  Window       = "<rect x='30' y='14' width='56' height='44' rx='4' fill='none' stroke='#8C93A0' stroke-width='6'/><rect x='14' y='38' width='56' height='46' rx='4' fill='BG' stroke='FG' stroke-width='7'/><rect x='14' y='38' width='56' height='10' fill='FG'/>"
  Freeform     = "<path d='M22 60 C10 30 40 12 58 22 C78 30 92 48 76 66 C62 82 34 84 22 60 Z' $d/>"
  Fullscreen   = "<g $s><polyline points='12,34 12,14 32,14'/><polyline points='68,14 88,14 88,34'/><polyline points='12,66 12,86 32,86'/><polyline points='68,86 88,86 88,66'/></g>"
  Folder       = "<path d='M10 28 h28 l8 8 h44 v46 h-80 Z' fill='#F0BE46'/><rect x='10' y='42' width='80' height='4' fill='BG'/>"
  Delay        = "<circle cx='50' cy='55' r='32' $s/><g $s><line x1='50' y1='55' x2='50' y2='36'/><line x1='50' y1='55' x2='64' y2='55'/><line x1='42' y1='14' x2='58' y2='14'/></g>"
  Cancel       = "<g fill='none' stroke='FG' stroke-width='10' stroke-linecap='round'><line x1='26' y1='26' x2='74' y2='74'/><line x1='74' y1='26' x2='26' y2='74'/></g>"
  LastCapture  = "<rect x='12' y='20' width='76' height='60' rx='5' $s/><polyline points='20,72 42,46 58,64 68,54 82,72' $s/><circle cx='68' cy='36' r='7' fill='#F0BE46'/>"
  VideoCamera  = "<rect x='10' y='30' width='56' height='42' rx='6' fill='FG'/><path d='M68 46 L90 32 V70 L68 56 Z' fill='FG'/>"
  VideoFolder  = "<path d='M10 28 h28 l8 8 h44 v46 h-80 Z' fill='#F0BE46'/><rect x='30' y='48' width='26' height='20' rx='3' fill='#E73C3C'/><path d='M58 54 L70 47 V69 L58 62 Z' fill='#E73C3C'/>"
  LastVideo    = "<rect x='12' y='20' width='76' height='60' rx='5' $s/><path d='M42 36 L66 50 L42 64 Z' fill='FG'/>"
  RecordDot    = "<circle cx='50' cy='50' r='30' fill='#E73C3C'/>"
  StopSquare   = "<rect x='26' y='26' width='48' height='48' rx='6' fill='FG'/>"
  PauseBars    = "<rect x='26' y='20' width='16' height='60' rx='3' fill='FG'/><rect x='58' y='20' width='16' height='60' rx='3' fill='FG'/>"
  Play         = "<path d='M32 20 L80 50 L32 80 Z' fill='FG'/>"
  Microphone   = "<rect x='38' y='10' width='24' height='46' rx='12' fill='FG'/><g $s><path d='M24 46 a26 26 0 0 0 52 0'/><line x1='50' y1='72' x2='50' y2='88'/><line x1='36' y1='88' x2='64' y2='88'/></g>"
  MicrophoneOff = "<rect x='38' y='10' width='24' height='46' rx='12' fill='FG'/><g $s><path d='M24 46 a26 26 0 0 0 52 0'/><line x1='50' y1='72' x2='50' y2='88'/></g><line x1='16' y1='16' x2='84' y2='84' stroke='BG' stroke-width='14'/><line x1='16' y1='16' x2='84' y2='84' stroke='#E73C3C' stroke-width='8' stroke-linecap='round'/>"
  Trash        = "<g $s><line x1='18' y1='26' x2='82' y2='26'/><line x1='40' y1='16' x2='60' y2='16'/><path d='M26 26 L30 86 H70 L74 26'/><line x1='43' y1='40' x2='43' y2='72'/><line x1='57' y1='40' x2='57' y2='72'/></g>"
  Pen          = "<line x1='22' y1='78' x2='76' y2='24' stroke='FG' stroke-width='14' stroke-linecap='round'/><line x1='18' y1='82' x2='30' y2='70' stroke='ACCENT' stroke-width='14' stroke-linecap='round'/>"
  Highlighter  = "<line x1='30' y1='70' x2='76' y2='24' stroke='FG' stroke-width='20' stroke-linecap='square'/><polygon points='18,74 30,62 38,70 26,82' fill='ACCENT'/><rect x='10' y='86' width='46' height='6' fill='ACCENT'/>"
  Eraser       = "<g transform='rotate(-45 50 50)'><rect x='22' y='36' width='56' height='28' rx='5' fill='FG'/><rect x='22' y='36' width='20' height='28' rx='5' fill='#8C93A0'/></g><line x1='12' y1='88' x2='88' y2='88' stroke='FG' stroke-width='5' stroke-linecap='round'/>"
  Undo         = "<g $s><path d='M30 38 H62 a20 20 0 0 1 0 40 H40'/><polyline points='42,24 28,38 42,52'/></g>"
  Redo         = "<g $s><path d='M70 38 H38 a20 20 0 0 0 0 40 H60'/><polyline points='58,24 72,38 58,52'/></g>"
  Copy         = "<rect x='14' y='30' width='44' height='54' rx='5' fill='none' stroke='#8C93A0' stroke-width='7'/><rect x='38' y='14' width='46' height='56' rx='5' fill='BG' stroke='FG' stroke-width='7'/>"
  Save         = "<path d='M16 14 H70 L86 30 V86 H16 Z' $s/><rect x='32' y='14' width='32' height='22' fill='FG'/><rect x='28' y='54' width='44' height='26' rx='3' fill='FG'/>"
  Share        = "<g fill='FG'><circle cx='26' cy='50' r='11'/><circle cx='74' cy='24' r='11'/><circle cx='74' cy='76' r='11'/></g><g $s><line x1='26' y1='50' x2='74' y2='24'/><line x1='26' y1='50' x2='74' y2='76'/></g>"
  Ruler        = "<g transform='rotate(-32 50 50)'><rect x='8' y='38' width='84' height='24' rx='3' fill='FG'/><g stroke='BG' stroke-width='3'><line x1='20' y1='38' x2='20' y2='50'/><line x1='32' y1='38' x2='32' y2='46'/><line x1='44' y1='38' x2='44' y2='50'/><line x1='56' y1='38' x2='56' y2='46'/><line x1='68' y1='38' x2='68' y2='50'/><line x1='80' y1='38' x2='80' y2='46'/></g></g>"
  ZoomIn       = "<circle cx='42' cy='42' r='26' $s/><line x1='62' y1='62' x2='86' y2='86' stroke='FG' stroke-width='10' stroke-linecap='round'/><g $s><line x1='30' y1='42' x2='54' y2='42'/><line x1='42' y1='30' x2='42' y2='54'/></g>"
  ZoomOut      = "<circle cx='42' cy='42' r='26' $s/><line x1='62' y1='62' x2='86' y2='86' stroke='FG' stroke-width='10' stroke-linecap='round'/><line x1='30' y1='42' x2='54' y2='42' $s/>"
  Zoom100      = "<circle cx='42' cy='42' r='26' $s/><line x1='62' y1='62' x2='86' y2='86' stroke='FG' stroke-width='10' stroke-linecap='round'/><text x='42' y='50' font-family='Segoe UI, Arial' font-weight='700' font-size='22' text-anchor='middle' fill='FG'>1:1</text>"
  ZoomFit      = "<g $s><polyline points='12,32 12,12 32,12'/><polyline points='68,12 88,12 88,32'/><polyline points='12,68 12,88 32,88'/><polyline points='68,88 88,88 88,68'/></g><rect x='30' y='34' width='40' height='32' rx='3' fill='FG'/>"
  Swatch       = "<circle cx='50' cy='50' r='34' fill='FG'/><circle cx='50' cy='50' r='28' fill='ACCENT'/>"
  Width        = "<line x1='14' y1='50' x2='86' y2='50' stroke='ACCENT' stroke-width='SW' stroke-linecap='round'/>"
  Text         = "<g $s><polyline points='20,30 20,18 80,18 80,30'/><line x1='50' y1='18' x2='50' y2='82'/><line x1='36' y1='82' x2='64' y2='82'/></g>"
  Redact       = "<g fill='FG'><rect x='12' y='22' width='76' height='14' rx='3'/><rect x='12' y='44' width='52' height='14' rx='3'/><rect x='12' y='66' width='64' height='14' rx='3'/></g><rect x='40' y='40' width='48' height='22' rx='3' fill='#E73C3C'/>"
  Settings     = "<circle cx='50' cy='50' r='14' $s/><g stroke='FG' stroke-width='10' stroke-linecap='round'><line x1='50' y1='12' x2='50' y2='24'/><line x1='50' y1='76' x2='50' y2='88'/><line x1='12' y1='50' x2='24' y2='50'/><line x1='76' y1='50' x2='88' y2='50'/><line x1='23' y1='23' x2='31' y2='31'/><line x1='69' y1='69' x2='77' y2='77'/><line x1='77' y1='23' x2='69' y2='31'/><line x1='31' y1='69' x2='23' y2='77'/></g>"
  Diagnostic   = "<polyline points='8,54 28,54 38,30 52,78 62,46 70,54 92,54' $s/>"
  Error        = "<path d='M50 12 L90 84 H10 Z' fill='#F5A028'/><rect x='46' y='36' width='8' height='26' rx='3' fill='BG'/><circle cx='50' cy='72' r='5' fill='BG'/>"
  Success      = "<polyline points='18,52 40,74 84,26' fill='none' stroke='#2ECC71' stroke-width='12' stroke-linecap='round' stroke-linejoin='round'/>"
  Empty        = ""
}
foreach ($k in $icons.Keys) {
  $svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100' width='100' height='100'>$($icons[$k])</svg>"
  Set-Content -Path (Join-Path $OutDir "$k.svg") -Value $svg -Encoding utf8 -NoNewline
}
"$($icons.Count) SVG written to $OutDir"
