param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipTests,

    [switch]$StopRunningApp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-MSBuild {
    $candidates = @(
        "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
        "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "MSBuild.exe was not found. Install .NET desktop build tools or Visual Studio."
}

function Get-RunningHexToPcapProcesses {
    $processes = Get-Process HexToPcap -ErrorAction SilentlyContinue
    if ($null -eq $processes) {
        return @()
    }

    $result = @()
    foreach ($process in $processes) {
        try {
            $path = $process.Path
        }
        catch {
            $path = $null
        }

        $result += [PSCustomObject]@{
            Id   = $process.Id
            Path = $path
        }
    }

    return $result
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path $repoRoot "HexToPcap.sln"
$appProjectPath = Join-Path $repoRoot "src\HexToPcap\HexToPcap.csproj"
$testProjectPath = Join-Path $repoRoot "tests\HexToPcap.Tests\HexToPcap.Tests.csproj"
$appOutputPath = Join-Path $repoRoot ("build\" + $Configuration + "\HexToPcap.exe")
$testOutputPath = Join-Path $repoRoot ("build\tests\" + $Configuration + "\HexToPcap.Tests.exe")

if (-not (Test-Path $appProjectPath)) {
    throw "App project file not found: $appProjectPath"
}

if (-not (Test-Path $testProjectPath)) {
    throw "Test project file not found: $testProjectPath"
}

$runningAppProcesses = @(Get-RunningHexToPcapProcesses | Where-Object {
    $_.Path -and $_.Path -like (Join-Path $repoRoot "build\*\HexToPcap.exe")
})

if ($runningAppProcesses.Count -gt 0) {
    if ($StopRunningApp) {
        foreach ($process in $runningAppProcesses) {
            Write-Host ("Stopping running HexToPcap process: " + $process.Id)
            Stop-Process -Id $process.Id -Force
        }
    }
    else {
        throw "HexToPcap.exe is running and may lock build output. Close it or rerun with -StopRunningApp."
    }
}

$msbuildPath = Find-MSBuild

Write-Host ("Using MSBuild: " + $msbuildPath)
Write-Host ("Building configuration: " + $Configuration)

if (Test-Path $solutionPath) {
    Write-Host ("Building solution: " + $solutionPath)
    & $msbuildPath $solutionPath /t:Build /p:Configuration=$Configuration /m
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host ("Solution file not found. Building projects directly.")
    & $msbuildPath $appProjectPath /t:Build /p:Configuration=$Configuration /m
    if ($LASTEXITCODE -ne 0) {
        throw "App build failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipTests) {
        & $msbuildPath $testProjectPath /t:Build /p:Configuration=$Configuration /m
        if ($LASTEXITCODE -ne 0) {
            throw "Test project build failed with exit code $LASTEXITCODE."
        }
    }
}

if (-not $SkipTests) {
    if (-not (Test-Path $testOutputPath)) {
        throw "Test executable not found: $testOutputPath"
    }

    Write-Host ("Running tests: " + $testOutputPath)
    & $testOutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path $appOutputPath)) {
    throw "Build succeeded but app output was not found: $appOutputPath"
}

Write-Host ""
Write-Host ("Build completed successfully.")
Write-Host ("Output: " + $appOutputPath)
Write-Host ("Tests: " + $testOutputPath)
