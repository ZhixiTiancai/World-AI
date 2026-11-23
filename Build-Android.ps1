param(
    [Parameter(Mandatory=$true)][string]$Jdk,
    [Parameter(Mandatory=$true)][string]$BuildTools,
    [Parameter(Mandatory=$true)][string]$AndroidJar,
    [Parameter(Mandatory=$true)][string]$Keystore,
    [string]$KeyAlias = 'worldai',
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist\WorldAI-android.apk')
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:WORLDAI_SIGNING_PASSWORD)) { throw 'Set WORLDAI_SIGNING_PASSWORD in the build environment.' }
if (!(Test-Path -LiteralPath $Keystore)) { throw 'The release keystore is required.' }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^([1-9][0-9]?)\.([0-9]{1,2})\.([0-9]{1,2})$') { throw 'VERSION must be major.minor.patch.' }
$code = [int]$Matches[1] * 10000 + [int]$Matches[2] * 100 + [int]$Matches[3]
$work = Join-Path $PSScriptRoot ('.build\android-' + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $work 'classes'
$dex = Join-Path $work 'dex'
$out = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $classes,$dex,(Split-Path -Parent $out) -Force | Out-Null
$app = Join-Path $PSScriptRoot 'android'
$env:JAVA_HOME = $Jdk
$env:Path = (Join-Path $Jdk 'bin') + [IO.Path]::PathSeparator + $env:Path

function Invoke-Compiler([string]$Tool, [string[]]$ToolArguments) {
    & $Tool @ToolArguments
    if ($LASTEXITCODE -ne 0) { throw "Build tool failed: $Tool ($LASTEXITCODE)" }
}

$javaSources = @(Get-ChildItem -LiteralPath (Join-Path $app 'src') -Filter '*.java' | ForEach-Object FullName)
Invoke-Compiler (Join-Path $Jdk 'bin\javac.exe') (@('-encoding','UTF-8','--release','8','-classpath',
    $AndroidJar,'-d',$classes) + $javaSources)
$jar = Join-Path $work 'classes.jar'
Invoke-Compiler (Join-Path $Jdk 'bin\jar.exe') @('cf',$jar,'-C',$classes,'.')
Invoke-Compiler (Join-Path $BuildTools 'd8.bat') @('--lib',$AndroidJar,'--min-api','26','--output',$dex,$jar)
$resources = Join-Path $work 'resources.zip'
$aapt = Join-Path $BuildTools 'aapt2.exe'
Invoke-Compiler $aapt @('compile','--dir',(Join-Path $app 'res'),'-o',$resources)
$unsigned = Join-Path $work 'unsigned.apk'
Invoke-Compiler $aapt @('link','-I',$AndroidJar,'--manifest',(Join-Path $app 'AndroidManifest.xml'),'-A',
    (Join-Path $app 'assets'),'--version-code',"$code",'--version-name',$version,'-o',$unsigned,$resources)
Invoke-Compiler (Join-Path $Jdk 'bin\jar.exe') @('uf',$unsigned,'-C',$dex,'classes.dex')
$aligned = Join-Path $work 'aligned.apk'
Invoke-Compiler (Join-Path $BuildTools 'zipalign.exe') @('-f','4',$unsigned,$aligned)
$signer = Join-Path $BuildTools 'apksigner.bat'
Invoke-Compiler $signer @('sign','--ks',$Keystore,'--ks-key-alias',$KeyAlias,'--ks-pass',
    'env:WORLDAI_SIGNING_PASSWORD','--v4-signing-enabled','false','--out',$out,$aligned)
Invoke-Compiler $signer @('verify','--verbose',$out)
Write-Host "Built $out"
