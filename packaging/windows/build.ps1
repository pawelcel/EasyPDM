# Buduje frontend i publikuje self-contained backend (win-x64, pojedynczy plik .exe) do
# packaging\windows\publish\ — uruchom PRZED kompilacją PdmSystem.iss w Inno Setup Compiler.
# Wymaga w PATH: .NET 10 SDK, Node.js/npm.
#
#   powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..\").Path
$PublishDir = Join-Path $PSScriptRoot "publish"

Write-Host "== Buduję frontend (npm run build) =="
Push-Location (Join-Path $RepoRoot "PdmSystem.Web")
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci nie powiodło się" }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build nie powiodło się" }
} finally {
    Pop-Location
}

Write-Host "== Publikuję backend (self-contained, win-x64) =="
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
dotnet publish (Join-Path $RepoRoot "PdmSystem.Api") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish nie powiodło się" }

Write-Host "== Gotowe: $PublishDir =="
Write-Host "Teraz skompiluj packaging\windows\PdmSystem.iss w Inno Setup Compiler (iscc.exe),"
Write-Host "np.: iscc packaging\windows\PdmSystem.iss"
