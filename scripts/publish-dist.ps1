# 一键发布绿色版 dist：杀残留进程 → 重新 publish → 清空并完整复制 → 校验必需文件
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:MSBUILDTERMINALLOGGER = 'off'
$env:MSBUILDDISABLEMSBUILDSERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'

Get-Process -Name 'AgentNotifier' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$pub = Join-Path $root 'src\AgentNotifier.App\bin\Release\net8.0-windows\publish'
$dist = Join-Path $root 'dist\AgentNotifier'

Remove-Item -Recurse -Force $pub -ErrorAction SilentlyContinue
$msbArgs = @('/t:Publish','/p:Configuration=Release','/p:DebugType=None','/p:DebugSymbols=false','/p:NuGetAudit=false','/v:q','/nologo','/m:1','/p:RestoreUseStaticGraphEvaluation=false','/p:UseSharedCompilation=false')
dotnet msbuild (Join-Path $root 'src\AgentNotifier.App\AgentNotifier.App.csproj') @msbArgs
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $pub '*') $dist -Force
$extra = Join-Path $root 'scripts\dist-extra'
if (Test-Path $extra) { Copy-Item (Join-Path $extra '*') $dist -Force }

$required = @(
  'AgentNotifier.exe','AgentNotifier.dll','AgentNotifier.runtimeconfig.json','AgentNotifier.deps.json',
  'AgentNotifier.Core.dll','AgentNotifier.Audio.dll','AgentNotifier.Notify.dll','AgentNotifier.Tools.dll',
  'uninstall.ps1','使用说明.txt'
)
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $dist $_)) })
if ($missing.Count -gt 0) { throw ('dist 缺少必需文件: ' + ($missing -join ', ')) }
Write-Output ('dist 发布完成并校验通过: ' + (Join-Path $dist 'AgentNotifier.exe'))