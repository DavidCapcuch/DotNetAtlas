#!/usr/bin/env pwsh
# Generates C# class from an Avro schema file (.avsc)
# Usage: ./generate-avro.ps1 <schema-file.avsc>
# Example: ./generate-avro.ps1 Avro/FeedbackChangedEvent.avsc

param(
    [Parameter(Mandatory = $true)]
    [string]$SchemaFile
)

$ErrorActionPreference = "Stop"

Write-Host "=== Avro C# Code Generation ===" -ForegroundColor Cyan

# Check if schema file exists
if (-not (Test-Path $SchemaFile))
{
    Write-Host "Schema file not found: $SchemaFile" -ForegroundColor Red
    exit 1
}

# Get full path and details
$SchemaPath = (Resolve-Path $SchemaFile).Path
$SchemaDir = Split-Path -Parent $SchemaPath
$SchemaName = [System.IO.Path]::GetFileNameWithoutExtension($SchemaPath)
$SchemaFileName = [System.IO.Path]::GetFileName($SchemaPath)

Write-Host "Schema file: $SchemaPath" -ForegroundColor Gray

# Parse namespace from Avro schema (for informational purposes only)
$SchemaContent = Get-Content $SchemaPath -Raw | ConvertFrom-Json
$Namespace = $SchemaContent.namespace

# Always output to the Avro folder in the same directory as the script
$ScriptDir = Split-Path -Parent $PSCommandPath
$OutputDir = Join-Path $ScriptDir "Avro"

# Ensure the Avro directory exists
if (-not (Test-Path $OutputDir))
{
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "Created Avro directory: $OutputDir" -ForegroundColor Gray
}

$OutputFile = Join-Path $OutputDir "$( $SchemaContent.name ).cs"

Write-Host "Schema namespace: $Namespace" -ForegroundColor Gray
Write-Host "Output directory: $OutputDir" -ForegroundColor Gray
Write-Host "Output file: $OutputFile" -ForegroundColor Gray

# Check if avrogen is installed
if (-not (Get-Command avrogen -ErrorAction SilentlyContinue))
{
    Write-Host "Installing Apache.Avro.Tools..." -ForegroundColor Yellow
    dotnet tool install --global Apache.Avro.Tools --version 1.12.0
}

Write-Host ""
Write-Host "Generating C# class for: $SchemaName" -ForegroundColor Cyan

try
{
    # avrogen generates output in the directory specified as second parameter
    # We want output in the Avro folder, avrogen will create namespace subdirectories
    avrogen -s $SchemaPath $OutputDir

    if (Test-Path $OutputFile)
    {
        Write-Host "Successfully generated: $( $SchemaContent.name ).cs" -ForegroundColor Green
        Write-Host "  Location: $OutputFile" -ForegroundColor Gray
        
        # Move the .avsc file to the same directory as the generated C# file
        $TargetAvscFile = Join-Path $OutputDir $SchemaFileName
        
        try {
            Move-Item -Path $SchemaPath -Destination $TargetAvscFile -Force
            Write-Host "Moved schema file to: $TargetAvscFile" -ForegroundColor Gray
        }
        catch {
            Write-Host "Warning: Could not move schema file: $_" -ForegroundColor Yellow
        }
    }
    else
    {
        Write-Host "Warning: Output file not found at: $OutputFile" -ForegroundColor Yellow
        Write-Host "  The file may have been generated in a different location" -ForegroundColor Yellow

        # Try to find the file by searching for it in the Avro folder
        $SearchPattern = "$( $SchemaContent.name ).cs"
        $FoundFiles = Get-ChildItem -Path $OutputDir -Recurse -Name $SearchPattern -ErrorAction SilentlyContinue

        if ($FoundFiles)
        {
            Write-Host "  Found file(s) at:" -ForegroundColor Yellow
            foreach ($file in $FoundFiles)
            {
                $FullPath = Join-Path $OutputDir $file
                Write-Host "    - $FullPath" -ForegroundColor Gray
                
                # Move the .avsc file to the same directory as the found C# file
                $FoundDir = Split-Path -Parent $FullPath
                $TargetAvscFile = Join-Path $FoundDir $SchemaFileName
                
                try {
                    Move-Item -Path $SchemaPath -Destination $TargetAvscFile -Force
                    Write-Host "  Moved schema file to: $TargetAvscFile" -ForegroundColor Gray
                }
                catch {
                    Write-Host "  Warning: Could not move schema file: $_" -ForegroundColor Yellow
                }
            }
        }
        else
        {
            Write-Host "  No files found matching: $SearchPattern in $OutputDir" -ForegroundColor Red
        }
    }
}
catch
{
    Write-Host "Failed to generate C# class" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Generation Complete ===" -ForegroundColor Cyan
