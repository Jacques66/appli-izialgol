<#
.SYNOPSIS
  Builds the Simula 67 & Algol 68 Studio distribution in the dist\ folder.

.DESCRIPTION
  Publishes as framework-dependent single files (.NET 8, win-x64):
    dist\Simula67Studio.exe   the integrated development environment (IDE)
    dist\Runner.exe           the launcher used as the base of standalone .exe files
    dist\simc.exe             command-line Simula 67 compiler
    dist\a68c.exe             command-line Algol 68 compiler
  then copies samples\ and docs\ into dist\.

.PARAMETER Configuration
  Release (default) or Debug.

.PARAMETER Test
  Also runs the tests (tests\IdeTests) and the IDE startup test.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
  .\build.ps1 -Test
#>
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [switch]$Test
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
$stage = Join-Path $root "out\publish"

function Step($message) { Write-Host "`n=== $message ===" -ForegroundColor Cyan }

function Publish($project, $name) {
    Step "Publishing $name"
    $out = Join-Path $stage $name
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    & dotnet publish (Join-Path $root $project) -c $Configuration -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
        -p:GenerateDocumentationFile=false -o $out --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "publishing $project failed" }
    Get-ChildItem $out -Filter *.exe | ForEach-Object { Copy-Item $_.FullName $dist -Force }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found: install the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)."
}

Step "Preparing $dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Get-ChildItem $dist -Force | Remove-Item -Recurse -Force
# dist\ is a build output: ignored by git
Set-Content -Path (Join-Path $dist ".gitignore") -Value "*" -Encoding ascii
New-Item -ItemType Directory -Force $stage | Out-Null

Publish "src\Runner\Runner.csproj" "Runner"
Publish "src\SimC\SimC.csproj" "simc"
Publish "src\A68C\A68C.csproj" "a68c"
Publish "src\Ide\Ide.csproj" "Simula67Studio"

Step "Copying samples and documentation"
foreach ($dir in "samples", "docs") {
    $src = Join-Path $root $dir
    if (Test-Path $src) { Copy-Item $src (Join-Path $dist $dir) -Recurse -Force }
    else { Write-Warning "$dir not found" }
}
$readme = Join-Path $root "README.md"
if (Test-Path $readme) { Copy-Item $readme $dist -Force }

Step "Checks"
$runner = Join-Path $dist "Runner.exe"
& $runner | Out-Null
if ($LASTEXITCODE -ne 2) { throw "Runner.exe without a program should return exit code 2 (got $LASTEXITCODE)" }
& (Join-Path $dist "simc.exe") --version
if ($LASTEXITCODE -ne 0) { throw "simc --version failed" }
& (Join-Path $dist "a68c.exe") --version
if ($LASTEXITCODE -ne 0) { throw "a68c --version failed" }

if ($Test) {
    Step "Tests (tests\IdeTests)"
    & dotnet run --project (Join-Path $root "tests\IdeTests") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "some tests failed" }

    Step "IDE startup test"
    $shot = Join-Path $stage "smoke.png"
    $p = Start-Process -FilePath (Join-Path $dist "Simula67Studio.exe") -ArgumentList "--smoke-test", "--screenshot", "`"$shot`"", "--self-test-dir", "`"$stage`"" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "the IDE startup test failed (see $stage\smoke-test.log)" }
    Write-Host "IDE started and closed without error (screenshot: $shot)"
}

Step "Done"
Get-ChildItem $dist | Format-Table Name, Length -AutoSize
Write-Host "Distribution ready in $dist" -ForegroundColor Green
