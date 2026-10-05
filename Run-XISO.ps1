param(
    [Parameter(Mandatory=$true)]
    [string]$GameFolder
)

$exe = ".\src\XISO.Toolkit\XISO.Toolkit\bin\Debug\net10.0\XISO.Toolkit.exe"

if (!(Test-Path $exe)) {
    Write-Host "XISO Toolkit executable not found. Build the solution first." -ForegroundColor Red
    exit 1
}

& $exe $GameFolder