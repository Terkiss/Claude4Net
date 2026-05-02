# Claude4Net-App Release Gate Verification Script
# Usage: .\scripts\verify-release.ps1

$ErrorActionPreference = "Stop"

function Run-Step {
    param (
        [string]$Name,
        [scriptblock]$Command
    )
    Write-Host "`n>>> Step: $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n[FAILURE] $Name failed with Exit Code: $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] $Name passed." -ForegroundColor Green
}

# 1. Standard Build
Run-Step "Standard Build" { dotnet build -p:UseAppHost=false }

# 2. Strict Nullable Build
Run-Step "Strict Nullable Build" { 
    dotnet build -p:UseAppHost=false -warnaserror:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8620,CS8625 
}

# 3. Unit & Integration Tests
Run-Step "Unit & Integration Tests" { 
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false 
}

# 4. CLI Smoke Test Verification
Write-Host "`n>>> Step: CLI Smoke Test Verification (--smoke-exit)" -ForegroundColor Cyan
$dllPath = Resolve-Path ".\Claude4Net.Cli\bin\Debug\net10.0\Claude4Net.Cli.dll"

if (-not (Test-Path $dllPath)) {
    Write-Host "[FAILURE] CLI DLL not found at $dllPath." -ForegroundColor Red
    exit 1
}

# Run with smoke flag and capture output
$stdout = dotnet $dllPath --smoke-exit
$cliExitCode = $LASTEXITCODE

$expectedMsg = "System is shutting down... Goodbye!"

if ($cliExitCode -eq 0 -and ($stdout -match $expectedMsg)) {
    Write-Host "[OK] CLI smoke test verified successfully." -ForegroundColor Green
    Write-Host "`n==============================================="
    Write-Host "[SUCCESS] Release Gate passed all checks." -ForegroundColor Green
    Write-Host "==============================================="
} else {
    Write-Host "`n[FAILURE] CLI Smoke Test failed." -ForegroundColor Red
    Write-Host "Exit Code: $cliExitCode" -ForegroundColor Yellow
    Write-Host "--- OUTPUT ---"
    Write-Host $stdout
    
    if ($stdout -match "Gemini API key is missing") {
        Write-Host "`n[CRITICAL] Gemini API key is missing. This is unexpected in smoke test mode." -ForegroundColor Red
    }
    
    exit 1
}
