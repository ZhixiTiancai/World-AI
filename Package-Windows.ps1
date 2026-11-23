param([string]$OutputPath = (Join-Path $PSScriptRoot 'dist\WorldAI-windows-x64.zip'))

$ErrorActionPreference = 'Stop'
$build = Join-Path $PSScriptRoot ('.build\windows-' + [Guid]::NewGuid().ToString('N'))
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $build,(Split-Path -Parent $output) -Force | Out-Null
$program = Join-Path $build 'WorldAI.exe'
$config = Join-Path $build 'WorldAI.exe.config'
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $program
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorldAI.exe.config') -Destination $config
Compress-Archive -LiteralPath $program,$config -DestinationPath $output -Force
Write-Host "Packaged $output"
