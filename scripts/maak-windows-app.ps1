# Maakt de Windows-app van het administratiepakket op een Windows-pc (met de .NET 10 SDK):
#   .\scripts\maak-windows-app.ps1                  (of -PgVersie 17.6-1 -Uit publiceer)
# Resultaat: publiceer\Boekerhouden-Administratie-win-x64.zip en de uitgepakte map "Boekerhouden Administratie".
param(
    [string]$Uit = "publiceer",
    [string]$PgVersie = "17.6-1"
)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$app = Join-Path $Uit "Boekerhouden Administratie"
$zip = Join-Path $Uit "Boekerhouden-Administratie-win-x64.zip"
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $Uit | Out-Null

Write-Host "App publiceren (win-x64, zelfstandig)..."
dotnet publish src/Boekerhouden.Administratie -c Release -r win-x64 --self-contained true -o $app -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "dotnet publish mislukt" }

Write-Host "PostgreSQL $PgVersie voor Windows ophalen..."
$pgZip = Join-Path $Uit "postgresql-$PgVersie-windows-x64-binaries.zip"
if (-not (Test-Path $pgZip)) {
    Invoke-WebRequest "https://get.enterprisedb.com/postgresql/postgresql-$PgVersie-windows-x64-binaries.zip" -OutFile $pgZip
}
$tijdelijk = Join-Path $Uit "pgsql-uitgepakt"
if (Test-Path $tijdelijk) { Remove-Item $tijdelijk -Recurse -Force }
Expand-Archive $pgZip -DestinationPath $tijdelijk
# Alleen wat de database nodig heeft: geen pgAdmin, documentatie, headers of symbolen.
foreach ($map in "bin", "lib", "share") {
    Copy-Item (Join-Path $tijdelijk "pgsql\$map") (Join-Path $app "pgsql\$map") -Recurse
}
Get-ChildItem (Join-Path $app "pgsql\lib") -Filter *.lib | Remove-Item
Remove-Item $tijdelijk -Recurse -Force
if (-not (Test-Path (Join-Path $app "pgsql\bin\initdb.exe"))) { throw "initdb.exe ontbreekt" }

Copy-Item scripts\windows\bureaublad.json, scripts\windows\LEESMIJ.txt $app

Write-Host "Zip maken..."
Compress-Archive -Path $app -DestinationPath $zip
Write-Host "Klaar: $zip"
