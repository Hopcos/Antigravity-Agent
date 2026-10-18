param(
    [int]$Port = 5055,
    [string]$HostName = "127.0.0.1",
    [string]$ApiKey = ""
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Starting GeminiAgent (Antigravity OpenAI Gateway)      " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Check dotnet CLI
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI is not found in PATH. Please install .NET 8.0 or .NET 10.0 SDK."
    exit 1
}

$dotnetVersion = dotnet --version
Write-Host "[i] Detected .NET SDK version: $dotnetVersion" -ForegroundColor Gray

# 2. Check if port is already in use
$activePort = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($activePort) {
    Write-Warning "Port $Port is already in use by PID $($activePort.OwningProcess)."
    Write-Host "[i] GeminiAgent is already running at http://$HostName`:$Port/v1" -ForegroundColor Green
    Write-Host "[i] Health check: http://$HostName`:$Port/v1/health" -ForegroundColor Cyan
    Write-Host "[i] To restart or stop it, run: .\stop_gateway.ps1 -Port $Port" -ForegroundColor Yellow
    exit 0
}

$url = "http://$HostName`:$Port"
Write-Host "[*] Gateway will listen on: $url" -ForegroundColor Cyan
Write-Host "[*] OpenAI compatible endpoint: $url/v1" -ForegroundColor Cyan
Write-Host "[*] Health check: $url/v1/health" -ForegroundColor Cyan

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $env:GATEWAY_API_KEY = $ApiKey
    Write-Host "[*] API Key protection enabled: $ApiKey" -ForegroundColor Magenta
}

$projectPath = Join-Path $PSScriptRoot "..\src\GeminiAgent\GeminiAgent.csproj"
dotnet run --project "$projectPath" --urls "$url"
