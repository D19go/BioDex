$ErrorActionPreference = 'Continue'
Write-Host "=== Fauna Lens - verificação do ambiente ===" -ForegroundColor Cyan

function Check($name, $command) {
    $cmd = Get-Command $command -ErrorAction SilentlyContinue
    if ($cmd) { Write-Host "[OK] $name -> $($cmd.Source)" -ForegroundColor Green }
    else { Write-Host "[FALTA] $name ($command)" -ForegroundColor Yellow }
}

Check ".NET" "dotnet"
Check "Java/JDK" "java"
Check "ADB" "adb"
Check "Git" "git"
Check "Python" "python"

Write-Host "`nVersões:" -ForegroundColor Cyan
if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --version }
if (Get-Command java -ErrorAction SilentlyContinue) { java -version }
if (Get-Command adb -ErrorAction SilentlyContinue) { adb version }
if (Get-Command python -ErrorAction SilentlyContinue) { python --version }

Write-Host "`nDepois, abra o projeto e execute: dotnet restore e dotnet build -c Release" -ForegroundColor Cyan
