param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist\WorldAI-windows-x64.zip'),
    [string]$SetupPath = (Join-Path $PSScriptRoot 'dist\WorldAI-Setup.exe')
)

$ErrorActionPreference = 'Stop'
$build = Join-Path $PSScriptRoot ('.build\windows-' + [Guid]::NewGuid().ToString('N'))
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $build,(Split-Path -Parent $output) -Force | Out-Null
$program = Join-Path $build 'WorldAI.exe'
$config = Join-Path $build 'WorldAI.exe.config'
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $program
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorldAI.exe.config') -Destination $config
Compress-Archive -LiteralPath $program,$config -DestinationPath $output -Force
$installer = [IO.Path]::GetFullPath($SetupPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $installer) -Force | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $installer -Setup
$archive = [IO.File]::OpenRead($output)
try {
    $setupStream = [IO.File]::Open($installer,[IO.FileMode]::Append,[IO.FileAccess]::Write)
    try {
        $archive.CopyTo($setupStream)
        $size = [BitConverter]::GetBytes([long]$archive.Length)
        $marker = [Text.Encoding]::ASCII.GetBytes('WorldAI.Bundle.v1')
        $setupStream.Write($size,0,$size.Length)
        $setupStream.Write($marker,0,$marker.Length)
    } finally { $setupStream.Dispose() }
} finally { $archive.Dispose() }
Write-Host "Packaged $installer and $output"
