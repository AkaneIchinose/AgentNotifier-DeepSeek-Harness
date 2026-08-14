# 单文件发布（需要 NuGet 网络：下载 runtime pack）；无网环境使用绿色目录发布（见 build.ps1 / dist）
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:MSBUILDTERMINALLOGGER = 'off'
$env:MSBUILDDISABLEMSBUILDSERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$self = if ($SelfContained) { 'true' } else { 'false' }
$msbArgs = @('/restore','/t:Publish','/p:Configuration=Release','/p:RuntimeIdentifier=win-x64','/p:PublishSingleFile=true',('/p:SelfContained=' + $self),'/p:DebugType=None','/p:DebugSymbols=false','/v:m','/nologo','/m:1','/p:RestoreUseStaticGraphEvaluation=false','/p:UseSharedCompilation=false')
dotnet msbuild (Join-Path $root 'src\AgentNotifier.App\AgentNotifier.App.csproj') @msbArgs
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
$pub = Join-Path $root 'src\AgentNotifier.App\bin\Release\net8.0-windows\win-x64\publish'
$dist = Join-Path $root 'dist\AgentNotifier'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $pub 'AgentNotifier.exe') (Join-Path $dist 'AgentNotifier.exe') -Force
Write-Output ('已发布单文件: ' + (Join-Path $dist 'AgentNotifier.exe'))
