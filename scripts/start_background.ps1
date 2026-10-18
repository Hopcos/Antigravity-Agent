param(
    [int]$Port = 5055,
    [string]$HostName = "127.0.0.1"
)

$ErrorActionPreference = "Stop"

# 1. 检查是否已经在运行
$existing = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[!] GeminiAgent is ALREADY RUNNING on port $Port (PID: $($existing.OwningProcess))" -ForegroundColor Yellow
    Write-Host "    Health check: http://$HostName`:$Port/v1/health" -ForegroundColor Cyan
    exit 0
}

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot "..\src\GeminiAgent\GeminiAgent.csproj")).Path
$logDir = Join-Path $PSScriptRoot "..\logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}
$stdoutLog = Join-Path $logDir "gateway_stdout.log"
$stderrLog = Join-Path $logDir "gateway_stderr.log"

$url = "http://$HostName`:$Port"

Write-Host "Starting GeminiAgent in background..." -ForegroundColor Cyan
Write-Host "Listening URL: $url" -ForegroundColor Gray
Write-Host "Log file:      $stdoutLog" -ForegroundColor Gray

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "dotnet"
$psi.Arguments = "run --project `"$projectPath`" --urls `"$url`""
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
$proc.Start() | Out-Null

Start-Sleep -Seconds 3

# 检查是否成功启动
$healthUrl = "$url/v1/health"
try {
    $resp = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 5
    Write-Host "`n[SUCCESS] GeminiAgent started successfully in background!" -ForegroundColor Green
    Write-Host "PID:            $($proc.Id)" -ForegroundColor Green
    Write-Host "Active Account: $($resp.account.email)" -ForegroundColor Green
    Write-Host "Token Status:   $($resp.account.remainingSeconds)s remaining (Auto perpetual renewal active)" -ForegroundColor Green
    Write-Host "OpenAI Base:    $url/v1" -ForegroundColor Green
} catch {
    Write-Warning "Gateway process started with PID $($proc.Id), but health endpoint did not respond immediately. Check $stderrLog"
}
