# Claude4Net-App Release Gate Verification Script
# Usage: .\scripts\verify-release.ps1

$ErrorActionPreference = "Stop"

# Environment Isolation to prevent real external API calls during verification
$env:GEMINI_API_KEY = "mock-gemini-key"
$env:ANTHROPIC_API_KEY = "mock-anthropic-key"


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

# 3.1 State Isolation Smoke
Run-Step "State Isolation Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K063|FullyQualifiedName~K064"
}

# 3.2 Spec Gate Smoke
Run-Step "Spec Gate Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K054|FullyQualifiedName~K055|FullyQualifiedName~K069|FullyQualifiedName~K070"
}

# 3.3 Provider Descriptor Smoke
Run-Step "Provider Descriptor Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K056|FullyQualifiedName~K057|FullyQualifiedName~K071|FullyQualifiedName~K072"
}

# 3.4 Routine Permission Smoke
Run-Step "Routine Permission Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K058|FullyQualifiedName~K059|FullyQualifiedName~K060|FullyQualifiedName~K074|FullyQualifiedName~K075|FullyQualifiedName~K076"
}

# 3.5 Dashboard Control Plane Smoke
Run-Step "Dashboard Control Plane Smoke" {
    dotnet test .\Claude4Net.Tests\Claude4Net.Tests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~K065|FullyQualifiedName~K066|FullyQualifiedName~K080|FullyQualifiedName~K081"
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
