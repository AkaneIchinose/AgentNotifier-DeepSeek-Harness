param([string]$LogDir, [string]$Template)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$today = Get-Date -Format 'yyyy-MM-dd'
$file = Join-Path $LogDir ($today + '.md')
if (-not (Test-Path $file)) {
    if (Test-Path $Template) {
        $tpl = Get-Content -Path $Template -Raw -Encoding UTF8
        $tpl = $tpl.Replace('{日期}', $today)
        Set-Content -Path $file -Value $tpl -Encoding UTF8
    } else {
        Set-Content -Path $file -Value ('# ' + $today) -Encoding UTF8
    }
}
Write-Output $file
