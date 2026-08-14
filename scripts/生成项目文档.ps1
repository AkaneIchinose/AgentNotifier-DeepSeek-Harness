param([string]$OutPath)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot '.docx-src'
if (-not (Test-Path $src)) { throw 'docx-src not found' }
if (Test-Path $OutPath) { Remove-Item $OutPath -Force }
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop | Out-Null } catch {}
[System.IO.Compression.ZipFile]::CreateFromDirectory($src, $OutPath)
$zip = [System.IO.Compression.ZipFile]::OpenRead($OutPath)
try {
    Write-Output ('ENTRIES: ' + (($zip.Entries | ForEach-Object { $_.FullName }) -join ', '))
    $doc = $zip.Entries | Where-Object { ($_.FullName -replace '\\','/') -eq 'word/document.xml' }
    $sr = New-Object System.IO.StreamReader($doc.Open())
    $xml = $sr.ReadToEnd()
    $sr.Dispose()
    $parsed = [xml]$xml
    Write-Output ('XML-PARSE: OK, paragraphs=' + @($parsed.document.body.p).Count)
} finally { $zip.Dispose() }
Write-Output ('SIZE: ' + (Get-Item $OutPath).Length)
