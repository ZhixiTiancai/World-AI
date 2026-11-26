param([string]$OutputPath = (Join-Path $PSScriptRoot 'WorldAI.exe'), [switch]$Setup)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.8 x64 compiler is required.' }
$wpf = Join-Path $framework 'WPF'
$references = @(
    (Join-Path $wpf 'WindowsBase.dll')
    (Join-Path $wpf 'PresentationCore.dll')
    (Join-Path $wpf 'PresentationFramework.dll')
    (Join-Path $framework 'System.Xaml.dll')
    (Join-Path $framework 'System.Web.Extensions.dll')
    (Join-Path $framework 'System.Net.Http.dll')
    (Join-Path $framework 'System.Security.dll')
    (Join-Path $framework 'System.IO.Compression.dll')
)
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', "/out:$OutputPath")
$arguments += if ($Setup) { '/main:WorldAI.SetupProgram' } else { '/main:WorldAI.Program' }
$arguments += "/win32icon:$PSScriptRoot\assets\WorldAI.ico"
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += "/resource:$PSScriptRoot\Shell.xaml,WorldAI.Shell.xaml"
$arguments += "/resource:$PSScriptRoot\Theme.xaml,WorldAI.Theme.xaml"
$arguments += "/resource:$PSScriptRoot\VERSION,WorldAI.Version"
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Host "Built $OutputPath"
