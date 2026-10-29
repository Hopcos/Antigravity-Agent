param(
    [int]$Port = 5055,
    [string]$HostName = "127.0.0.1"
)

$ErrorActionPreference = "Stop"

# 1. Check if port is already active
$existing = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[!] AntigravityGateway is ALREADY RUNNING on port $Port (PID: $($existing.OwningProcess))" -ForegroundColor Yellow
    Write-Host "    Health check: http://$HostName`:$Port/v1/health" -ForegroundColor Cyan
    exit 0
}

$slnPath = (Resolve-Path (Join-Path $PSScriptRoot "..\AntigravityGateway.sln")).Path
$binDir = (Resolve-Path (Join-Path $PSScriptRoot "..\src\AntigravityGateway\bin\Debug\net10.0")).Path
$exePath = Join-Path $binDir "AntigravityGateway.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Building AntigravityGateway.sln..." -ForegroundColor Gray
    dotnet build "$slnPath" | Out-Null
}

$logDir = Join-Path $PSScriptRoot "..\logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$stdoutLog = (Join-Path $logDir "gateway_stdout.log")
$stderrLog = (Join-Path $logDir "gateway_stderr.log")

$url = "http://$HostName`:$Port"

Write-Host "Starting AntigravityGateway in background..." -ForegroundColor Cyan
Write-Host "Executable:    $exePath" -ForegroundColor Gray
Write-Host "Listening URL: $url" -ForegroundColor Gray
Write-Host "Log file:      $stdoutLog" -ForegroundColor Gray

$proc = Start-Process -FilePath $exePath `
    -ArgumentList "--urls `"$url`"" `
    -WorkingDirectory $binDir `
    -RedirectStandardOutput $stdoutLog `
    -RedirectStandardError $stderrLog `
    -WindowStyle Hidden `
    -PassThru

# Wait for health endpoint (up to 10 seconds)
$healthUrl = "$url/v1/health"
$ready = $false
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Seconds 1
    try {
        $resp = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2
        if ($resp.status -eq "healthy") {
            $ready = $true
            break
        }
    } catch {
        # Keep waiting
    }
}

if ($ready) {
    Write-Host "`n[SUCCESS] AntigravityGateway started successfully in background!" -ForegroundColor Green
    Write-Host "PID:            $($proc.Id)" -ForegroundColor Green
    Write-Host "Active Account: $($resp.account.email)" -ForegroundColor Green
    Write-Host "Token Status:   $($resp.account.remainingSeconds)s remaining (Auto perpetual renewal active)" -ForegroundColor Green
    Write-Host "OpenAI Base:    $url/v1" -ForegroundColor Green
} else {
    Write-Warning "Gateway process started with PID $($proc.Id), but health endpoint did not respond within 10s."
    if (Test-Path $stderrLog) {
        Get-Content $stderrLog -Tail 20 | Write-Warning
    }
}
