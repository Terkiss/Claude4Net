#!/bin/bash
# Claude4Net macOS/Linux Release Gate Script
# (macOS support prepared, native verification pending)

set -e

echo ">>> Step 1: Standard Build"
dotnet build -p:UseAppHost=false

echo ">>> Step 2: Strict Nullable Build (Warnings as Errors)"
dotnet build -p:UseAppHost=false -warnaserror:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8620,CS8625

echo ">>> Step 3: Unit & Integration Tests"
dotnet test ./Claude4Net.Tests/Claude4Net.Tests.csproj -p:UseAppHost=false

echo ">>> Step 4: CLI Smoke Test Verification (--smoke-exit)"
# Run via dotnet DLL to avoid AppHost requirement on different OS
dotnet ./Claude4Net.Cli/bin/Debug/net10.0/Claude4Net.Cli.dll --smoke-exit

echo "==============================================="
echo "[SUCCESS] macOS/Linux Release Gate passed all checks."
echo "(Note: Native verification pending on actual macOS hardware)"
echo "==============================================="
