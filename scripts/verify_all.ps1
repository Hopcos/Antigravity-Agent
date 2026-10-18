param(
    [string]$BaseUrl = "http://127.0.0.1:5055/v1"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Running GeminiAgent Verification Suite                " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target Base URL: $BaseUrl" -ForegroundColor Gray

# 1. Check Gateway Health
$healthUrl = "$BaseUrl/health"
Write-Host "`n[Step 1/3] Checking Gateway Health Endpoint ($healthUrl)..." -ForegroundColor Yellow
try {
    $healthResp = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 5
    Write-Host "Gateway status: $($healthResp.status)" -ForegroundColor Green
    Write-Host "Active account: $($healthResp.account)" -ForegroundColor Green
    Write-Host "Token source:   $($healthResp.token_source)" -ForegroundColor Green
    Write-Host "Token expires:  $($healthResp.token_expires_at)" -ForegroundColor Green
    Write-Host "Models count:   $($healthResp.models.Count)" -ForegroundColor Green
} catch {
    Write-Error "Failed to connect to gateway at $healthUrl. Is GeminiAgent running?"
    exit 1
}

# 2. Run Comprehensive Functional Test (HTTP SSE + Non-streaming + Tools)
Write-Host "`n[Step 2/3] Running tests\test_gateway_openai.py..." -ForegroundColor Yellow
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) {
    Write-Error "python is not found in PATH."
    exit 1
}

& python (Join-Path $PSScriptRoot "..\tests\test_gateway_openai.py")
if ($LASTEXITCODE -ne 0) {
    Write-Error "test_gateway_openai.py failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 3. Run Official OpenAI Python SDK Test
Write-Host "`n[Step 3/3] Running tests\test_official_openai_sdk.py..." -ForegroundColor Yellow
& python (Join-Path $PSScriptRoot "..\tests\test_official_openai_sdk.py")
if ($LASTEXITCODE -ne 0) {
    Write-Error "test_official_openai_sdk.py failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "   ALL TESTS PASSED SUCCESSFULLY! (100% PASS RATE)       " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
