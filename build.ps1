<#
.SYNOPSIS
  Construit la distribution de Simula 67 & Algol 68 Studio dans le dossier dist\.

.DESCRIPTION
  Publie en mono-fichier dependant du framework (.NET 8, win-x64) :
    dist\Simula67Studio.exe   l'environnement de developpement (IDE)
    dist\Runner.exe           le lanceur qui sert de base aux .exe autonomes
    dist\simc.exe             compilateur Simula 67 en ligne de commande
    dist\a68c.exe             compilateur Algol 68 en ligne de commande
  puis copie samples\ et docs\ dans dist\.

.PARAMETER Configuration
  Release (defaut) ou Debug.

.PARAMETER Test
  Lance aussi les tests (tests\IdeTests) et le test de demarrage de l'IDE.

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
    Step "Publication de $name"
    $out = Join-Path $stage $name
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    & dotnet publish (Join-Path $root $project) -c $Configuration -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
        -p:GenerateDocumentationFile=false -o $out --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "echec de la publication de $project" }
    Get-ChildItem $out -Filter *.exe | ForEach-Object { Copy-Item $_.FullName $dist -Force }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet introuvable : installez le SDK .NET 8 (https://dotnet.microsoft.com/download/dotnet/8.0)."
}

Step "Preparation de $dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Get-ChildItem $dist -Force | Remove-Item -Recurse -Force
# dist\ est un produit de construction : ignore par git
Set-Content -Path (Join-Path $dist ".gitignore") -Value "*" -Encoding ascii
New-Item -ItemType Directory -Force $stage | Out-Null

Publish "src\Runner\Runner.csproj" "Runner"
Publish "src\SimC\SimC.csproj" "simc"
Publish "src\A68C\A68C.csproj" "a68c"
Publish "src\Ide\Ide.csproj" "Simula67Studio"

Step "Copie des exemples et de la documentation"
foreach ($dir in "samples", "docs") {
    $src = Join-Path $root $dir
    if (Test-Path $src) { Copy-Item $src (Join-Path $dist $dir) -Recurse -Force }
    else { Write-Warning "$dir introuvable" }
}
$readme = Join-Path $root "README.md"
if (Test-Path $readme) { Copy-Item $readme $dist -Force }

Step "Verifications"
$runner = Join-Path $dist "Runner.exe"
& $runner | Out-Null
if ($LASTEXITCODE -ne 2) { throw "Runner.exe sans programme devrait rendre le code 2 (obtenu $LASTEXITCODE)" }
& (Join-Path $dist "simc.exe") --version
if ($LASTEXITCODE -ne 0) { throw "simc --version a echoue" }
& (Join-Path $dist "a68c.exe") --version
if ($LASTEXITCODE -ne 0) { throw "a68c --version a echoue" }

if ($Test) {
    Step "Tests (tests\IdeTests)"
    & dotnet run --project (Join-Path $root "tests\IdeTests") -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "des tests ont echoue" }

    Step "Test de demarrage de l'IDE"
    $shot = Join-Path $stage "smoke.png"
    $p = Start-Process -FilePath (Join-Path $dist "Simula67Studio.exe") -ArgumentList "--smoke-test", "--screenshot", "`"$shot`"", "--self-test-dir", "`"$stage`"" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "le test de demarrage de l'IDE a echoue (voir $stage\smoke-test.log)" }
    Write-Host "IDE demarre et ferme sans erreur (capture : $shot)"
}

Step "Termine"
Get-ChildItem $dist | Format-Table Name, Length -AutoSize
Write-Host "Distribution prete dans $dist" -ForegroundColor Green
