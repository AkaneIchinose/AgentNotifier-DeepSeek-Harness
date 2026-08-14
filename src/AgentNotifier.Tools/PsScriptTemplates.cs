namespace AgentNotifier.Tools;

/// <summary>运行时生成的 PowerShell 辅助脚本模板（写入 %APPDATA%\AgentNotifier\）</summary>
public static class PsScriptTemplates
{
    /// <summary>hook 上报脚本：读取 stdin JSON，携带令牌 POST 到本机事件服务</summary>
    public const string NotifyHelper =
@"param([string]$Kind = 'needs_user', [string]$Tool = 'cli')
$ErrorActionPreference = 'SilentlyContinue'
$base = Join-Path $env:APPDATA 'AgentNotifier'
$token = ''
$port = 28150
$tokenFile = Join-Path $base 'token.txt'
$portFile = Join-Path $base 'port.txt'
if (Test-Path $tokenFile) { $token = (Get-Content $tokenFile -Raw -Encoding UTF8).Trim() }
if (Test-Path $portFile) { $port = [int]((Get-Content $portFile -Raw).Trim()) }
$summary = ''
$sessionId = ''
try {
  $raw = [Console]::In.ReadToEnd()
  if ($raw) {
    $j = $raw | ConvertFrom-Json
    if ($j.session_id) { $sessionId = [string]$j.session_id }
    elseif ($j.sessionId) { $sessionId = [string]$j.sessionId }
    if ($j.hook_event_name) { $summary = [string]$j.hook_event_name }
    elseif ($j.event) { $summary = [string]$j.event }
    elseif ($j.transcript_path) { $summary = [IO.Path]::GetFileName([string]$j.transcript_path) }
    elseif ($j.title) { $summary = [string]$j.title }
    # PreToolUse 过滤：AskUserQuestion=提问直接上报；其他工具仅权限请求(ask)上报；自动允许的调用静默
    if ($j.tool_name) {
      $toolName = [string]$j.tool_name
      if ($toolName -ne 'AskUserQuestion' -and $j.permissionDecision -and [string]$j.permissionDecision -ne 'ask') { exit 0 }
    }
  }
} catch { }
if (-not $token) { exit 0 }
$body = @{ tool = $Tool; kind = $Kind; sessionId = $sessionId; summary = $summary; ts = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() } | ConvertTo-Json -Compress
$bodyFile = Join-Path $env:TEMP ('agentnotifier-body-' + [guid]::NewGuid().ToString('N') + '.json')
try {
  [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
  curl.exe -s -o NUL -X POST (""http://127.0.0.1:{0}/v1/event"" -f $port) -H (""Authorization: Bearer {0}"" -f $token) -H 'Content-Type: application/json' --data-binary (""@{0}"" -f $bodyFile)
} catch { }
try { Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue } catch { }
exit 0
";

    /// <summary>系统 Toast 脚本（WinRT，经 PowerShell 调用）：标题/正文经环境变量传入，避免引号注入</summary>
    public const string ToastHelper =
@"  param([string]$Title, [string]$Body)
  $ErrorActionPreference = 'SilentlyContinue'
  try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
    $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime]
    $lnkPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AgentNotifier.lnk'
    try {
      if (-not (Test-Path $lnkPath)) {
        $shell = New-Object -ComObject WScript.Shell
        $lnk = $shell.CreateShortcut($lnkPath)
        $lnk.TargetPath = [Environment]::SystemDirectory + '\cmd.exe'
        $lnk.Save()
      }
    } catch { }
    # 注册表注册 AUMID（未打包应用显示 Toast 的标准方式）
    try {
      $regPath = 'HKCU:\Software\Classes\AppUserModelId\AgentNotifier'
      if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
        New-ItemProperty -Path $regPath -Name 'DisplayName' -PropertyType String -Value 'AgentNotifier' -Force | Out-Null
      }
    } catch { }
    $esc = { param($s) ([System.Security.SecurityElement]::Escape($s)) }
    $xml = '<toast><visual><binding template=""ToastGeneric""><text>' + (& $esc $Title) + '</text><text>' + (& $esc $Body) + '</text></binding></visual></toast>'
    $docType = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType=WindowsRuntime]
    $doc = $docType::new()
    $doc.LoadXml($xml)
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier([string]'AgentNotifier')
    $toastType = [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType=WindowsRuntime]
    $toast = $toastType::new($doc)
    $notifier.Show($toast)
    exit 0
  } catch {
  try { [System.IO.File]::AppendAllText((Join-Path $env:APPDATA 'AgentNotifier\toast-fail.log'), (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $_.Exception.Message + [Environment]::NewLine) } catch { }
  exit 1
}
  ";

    /// <summary>一键恢复脚本：删除已写入的 hooks、还原备份、清理辅助文件</summary>
    public const string Uninstall =
@"# AgentNotifier 一键恢复脚本（uninstall）
# 作用：删除本软件写入的 Claude Code hooks 配置，并用备份还原原始文件；清理辅助文件。
# 用法：powershell -ExecutionPolicy Bypass -File uninstall.ps1   （加 -Full 可同时删除软件配置与日志）
param([switch]$Full)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:APPDATA 'AgentNotifier'
function Restore-Config([string]$path, [string]$label) {
  if (-not (Test-Path $path)) { Write-Output (""[跳过] {0} 配置文件不存在"" -f $label); return }
  $bak = Join-Path (Join-Path $base 'backups') ((Split-Path $path -Leaf) + '.bak')
  if (Test-Path $bak) {
    Copy-Item $bak $path -Force
    Write-Output (""[完成] {0} 已还原备份"" -f $label)
  } else {
    Remove-Item $path -Force
    Write-Output (""[完成] {0} 无备份，已删除配置（如为空文件）"" -f $label)
  }
}
Restore-Config (Join-Path $env:USERPROFILE '.claude\settings.json') 'Claude Code'
$helper = Join-Path $base 'notify.ps1'
if (Test-Path $helper) { Remove-Item $helper -Force; Write-Output '[完成] 已删除 notify.ps1 上报脚本' }
foreach ($f in @('token.txt','port.txt')) {
  $p = Join-Path $base $f
  if (Test-Path $p) { Remove-Item $p -Force; Write-Output (""[完成] 已删除 {0}"" -f $f) }
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
";
}
