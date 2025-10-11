#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run tests with code coverage and generate a unified HTML report.

.DESCRIPTION
    This script runs all tests with code coverage collection, generates
    a HTML report using ReportGenerator and opens it.

.PARAMETER CleanTestResults
    Clean generated test results per project used for generating aggregated reports (default: true).

.PARAMETER ReportTypes
    Report types to generate. Default: "HtmlInline"
    See https://github.com/danielpalme/ReportGenerator/wiki/Output-formats
    for available options.

.EXAMPLE
    .\test-coverage.ps1
    Run tests, generate report, and open in browser.

.EXAMPLE
    .\test-coverage.ps1 -CleanTestResults:$false
    Run tests without cleaning test results per project after.
#>

param(
    [switch]$CleanTestResults = $true,
    [string]$ReportTypes = "HtmlInline"
)

$ErrorActionPreference = "Stop"

# Navigate to repository root (parent of test folder)
$testDir = $PSScriptRoot
$coverageReportDir = Join-Path $testDir "coveragereport"

Write-Host "Code Coverage Report Generator" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host "Report Dir: $coverageReportDir`n" -ForegroundColor Gray

# Install reportgenerator if not installed
$reportGeneratorInstalled = $null -ne (Get-Command reportgenerator -ErrorAction SilentlyContinue)
if (-not $reportGeneratorInstalled) {
    Write-Host "[!] ReportGenerator not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g dotnet-reportgenerator-globaltool
    Write-Host "[+] ReportGenerator installed successfully!`n" -ForegroundColor Green
}

# Clean previous coverage results
if (Test-Path $coverageReportDir) {
    Write-Host "[*] Cleaning previous coverage report..." -ForegroundColor Yellow
    Remove-Item -Path $coverageReportDir -Recurse -Force
}

# Clean old test results (default behavior)
if ($CleanTestResults) {
    Write-Host "[*] Cleaning old test results..." -ForegroundColor Yellow
    Get-ChildItem -Path $testDir -Recurse -Directory -Filter "TestResults" | Remove-Item -Recurse -Force
    Write-Host "[+] Old test results cleaned!`n" -ForegroundColor Green
}

# Run tests with coverage
Write-Host "[*] Running tests with coverage collection...`n" -ForegroundColor Cyan

dotnet test .. --collect:"XPlat Code Coverage" --settings coverlet.runsettings

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[-] Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n[+] Tests completed successfully!`n" -ForegroundColor Green

# Find all coverage files
$coverageFiles = Get-ChildItem -Path . -Recurse -Filter "coverage.cobertura.xml" | Select-Object -ExpandProperty FullName

if ($coverageFiles.Count -eq 0) {
    Write-Host "[-] No coverage files found! Run tests first." -ForegroundColor Red
    exit 1
}

Write-Host "[*] Found $($coverageFiles.Count) coverage file(s)`n" -ForegroundColor Cyan
foreach ($file in $coverageFiles) {
    Write-Host "   - $file" -ForegroundColor Gray
}

# Generate report
Write-Host "[*] Generating unified coverage report...`n" -ForegroundColor Cyan

$reports = ($coverageFiles -join ";")

reportgenerator `
    -reports:$reports `
    -targetdir:$coverageReportDir `
    -reporttypes:$ReportTypes `
    -verbosity:"Info"

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[-] Report generation failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n[+] Coverage report generated successfully!`n" -ForegroundColor Green

# Display summary from Cobertura XML
$coberturaFile = Join-Path $coverageReportDir "Cobertura.xml"
if (Test-Path $coberturaFile) {
    try {
        [xml]$coverage = Get-Content $coberturaFile
        $lineRate = [math]::Round([double]$coverage.coverage.'line-rate' * 100, 2)
        $branchRate = [math]::Round([double]$coverage.coverage.'branch-rate' * 100, 2)

        Write-Host "[*] Coverage Summary:" -ForegroundColor Cyan
        Write-Host "====================" -ForegroundColor Cyan
        Write-Host "  Line Coverage:   $lineRate%" -ForegroundColor $(if ($lineRate -ge 80) { "Green" } elseif ($lineRate -ge 60) { "Yellow" } else { "Red" })
        Write-Host "  Branch Coverage: $branchRate%" -ForegroundColor $(if ($branchRate -ge 80) { "Green" } elseif ($branchRate -ge 60) { "Yellow" } else { "Red" })
        Write-Host ""
    }
    catch {
        Write-Host "[!] Could not parse coverage summary`n" -ForegroundColor Yellow
    }
}

# Open in browser
$reportPath = Join-Path $coverageReportDir "index.html"
Write-Host "[*] Opening coverage report in browser..." -ForegroundColor Cyan
Write-Host "    $reportPath" -ForegroundColor Gray
Start-Process $reportPath

# Clean up test results after report generation
Write-Host "`n[*] Cleaning up test results..." -ForegroundColor Yellow
Get-ChildItem -Path $testDir -Recurse -Directory -Filter "TestResults" | Remove-Item -Recurse -Force
Write-Host "[+] Test results cleaned up!" -ForegroundColor Green

Write-Host "`n[+] Done!" -ForegroundColor Green

