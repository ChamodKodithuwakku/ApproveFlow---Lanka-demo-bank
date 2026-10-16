<#
.SYNOPSIS
    One-time developer setup for ApproveFlow. Stores secrets with dotnet user-secrets (never in the repository).

.EXAMPLE
    ./scripts/setup-dev.ps1                       # demo password + JWT key (enough for SQLite demo mode)
    ./scripts/setup-dev.ps1 -LocalDb              # also point the app at SQL Server LocalDB
    ./scripts/setup-dev.ps1 -Docker               # also create .env and point the app at the docker-compose SQL Server
    ./scripts/setup-dev.ps1 -DemoPassword "My-Own#Pass2026"
#>
[CmdletBinding()]
param(
    [string] $DemoPassword = "ApproveFlow@2026",
    [switch] $LocalDb,
    [switch] $Docker,
    [string] $AiApiKey
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/ApproveFlow.Web"

function New-Secret([int] $length) {
    $chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789".ToCharArray()
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Set-Secret([string] $key, [string] $value) {
    dotnet user-secrets set $key $value --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to set user-secret $key" }
    Write-Host "  set $key"
}

Write-Host "Configuring user-secrets for ApproveFlow.Web..." -ForegroundColor Cyan
Set-Secret "Seed:DemoPassword" $DemoPassword
Set-Secret "Jwt:SigningKey" (New-Secret 64)

if ($LocalDb) {
    Set-Secret "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=ApproveFlow;Trusted_Connection=True;TrustServerCertificate=True"
}

if ($Docker) {
    $envFile = Join-Path $root ".env"
    if (-not (Test-Path $envFile)) {
        $sa = (New-Secret 20) + "#9a"
        Set-Content -Path $envFile -Value "MSSQL_SA_PASSWORD=$sa" -Encoding ascii
        Write-Host "  created .env with a random SA password"
    }
    $sa = ((Get-Content $envFile | Where-Object { $_ -like "MSSQL_SA_PASSWORD=*" }) -split "=", 2)[1]
    Set-Secret "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=ApproveFlow;User Id=sa;Password=$sa;TrustServerCertificate=True"
}

if ($AiApiKey) {
    Set-Secret "AiRiskSummary:ApiKey" $AiApiKey
}

Write-Host ""
Write-Host "Done. Demo accounts (admin@demo.lk, maker1@demo.lk, checker1@demo.lk, ...) use the password: $DemoPassword" -ForegroundColor Green
if ($LocalDb -or $Docker) {
    Write-Host "Run:  dotnet run --project src/ApproveFlow.Web --launch-profile http     (http://localhost:5001)"
} else {
    Write-Host "Run:  dotnet run --project src/ApproveFlow.Web --launch-profile sqlite-demo   (http://localhost:5080)"
}
