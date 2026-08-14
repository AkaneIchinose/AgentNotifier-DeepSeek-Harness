# AgentNotifier 一键恢复脚本（uninstall）
# 作用：删除本软件写入的 Claude Code / opencode hooks 配置，并用备份还原原始文件；清理辅助文件。
# 用法：powershell -ExecutionPolicy Bypass -File uninstall.ps1   （加 -Full 可同时删除软件配置与日志）
param([switch]$Full)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:APPDATA 'AgentNotifier'
function Restore-Config([string]$path, [string]$label) {
  if (-not (Test-Path $path)) { Write-Output ("[跳过] {0} 配置文件不存在" -f $label); return }
  $bak = Join-Path (Join-Path $base 'backups') ((Split-Path $path -Leaf) + '.bak')
  if (Test-Path $bak) {
    Copy-Item $bak $path -Force
    Write-Output ("[完成] {0} 已还原备份" -f $label)
  } else {
    Remove-Item $path -Force
    Write-Output ("[完成] {0} 无备份，已删除配置（如为空文件）" -f $label)
  }
}
Restore-Config (Join-Path $env:USERPROFILE '.claude\settings.json') 'Claude Code'
Restore-Config (Join-Path $env:USERPROFILE '.config\opencode\opencode.json') 'opencode'
$helper = Join-Path $base 'notify.ps1'
if (Test-Path $helper) { Remove-Item $helper -Force; Write-Output '[完成] 已删除 notify.ps1 上报脚本' }
foreach ($f in @('token.txt','port.txt')) {
  $p = Join-Path $base $f
  if (Test-Path $p) { Remove-Item $p -Force; Write-Output ("[完成] 已删除 {0}" -f $f) }
}
$lnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AgentNotifier.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Output '[完成] 已删除开始菜单快捷方式' }
if ($Full) {
  $cfg = Join-Path $base 'config.json'
  if (Test-Path $cfg) { Remove-Item $cfg -Force; Write-Output '[完成] 已删除 config.json（软件设置）' }
  $logs = Join-Path $base 'logs'
  if (Test-Path $logs) { Remove-Item $logs -Recurse -Force; Write-Output '[完成] 已删除日志目录' }
}
Write-Output '全部完成：AgentNotifier 写入的配置已恢复原样。'