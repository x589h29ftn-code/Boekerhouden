# Maakt de Windows-app van het administratiepakket op een Windows-pc (met de .NET 10 SDK):
#   .\scripts\maak-windows-app.ps1                  (of -Uit publiceer)
# Resultaat: publiceer\Boekerhouden-Administratie-win-x64.zip en de map "Boekerhouden Administratie".
param([string]$Uit = "publiceer")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$app = Join-Path $Uit "Boekerhouden Administratie"
$zip = Join-Path $Uit "Boekerhouden-Administratie-win-x64.zip"
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $Uit | Out-Null

Write-Host "Windows-app publiceren (win-x64, zelfstandig)..."
dotnet publish src/Boekerhouden.Administratie.Windows -c Release -r win-x64 --self-contained true -o $app -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish mislukt" }

# Wat van de webapp meekomt maar hier niet gebruikt wordt.
foreach ($bestand in "Boekerhouden.Administratie.exe", "Boekerhouden.Administratie.runtimeconfig.json", "appsettings.json", "appsettings.Development.json", "web.config") {
    $pad = Join-Path $app $bestand
    if (Test-Path $pad) { Remove-Item $pad }
}
Copy-Item scripts\windows\LEESMIJ.txt $app

Write-Host "Zip maken..."
Compress-Archive -Path $app -DestinationPath $zip
Write-Host "Klaar: $zip"
