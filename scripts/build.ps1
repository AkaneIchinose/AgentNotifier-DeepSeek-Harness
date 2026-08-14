param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:MSBUILDTERMINALLOGGER = 'off'
$env:MSBUILDDISABLEMSBUILDSERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$msbArgs = @('/restore','/t:Build',('/p:Configuration=' + $Configuration),'/v:m','/nologo','/m:1','/p:RestoreUseStaticGraphEvaluation=false','/p:UseSharedCompilation=false','/p:NuGetAudit=false')
dotnet msbuild (Join-Path $root 'src\AgentNotifier.sln') @msbArgs
exit $LASTEXITCODE
