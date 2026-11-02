$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'publish\FaunaPackBuilder.exe'
if (-not (Test-Path $exe)) {
    throw "Executável não encontrado. Execute .\build.ps1 primeiro."
}
Set-Location (Split-Path -Parent $exe)
& $exe
exit $LASTEXITCODE
