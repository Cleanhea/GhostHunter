<#
.SYNOPSIS
    Runs the Unity tests (or a compile check) in batchmode and prints a short summary.

.DESCRIPTION
    - If the Unity editor has this project open (Temp/UnityLockfile is locked), Assets, Packages
      and ProjectSettings are mirrored to a verification clone and the run happens there.
      Default clone: %USERPROFILE%\GHV (a short path avoids the Burst long-path error).
      The clone keeps its own Library, so only the first run pays for the full import.
    - The Unity log goes to a file only. The console gets the counts, failed tests with the first
      line of each message, and up to five skipped tests.
    - Output files: Logs/tests/<Platform>-results.xml and Logs/tests/<Platform>.log in this checkout.
    - Exit code 0 means every requested run finished and nothing failed.

    Docs: docs/workflow/testing.md section 5.2.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform All
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform PlayMode -Filter "VoicePlaybackTests"
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Platform Compile
#>
[CmdletBinding()]
param(
    [ValidateSet('EditMode', 'PlayMode', 'All', 'Compile')]
    [string]$Platform = 'EditMode',

    # Passed to Unity -testFilter (test name, class, namespace or regex; ';' separates several).
    [string]$Filter = '',

    # Auto: use the clone only while the editor holds this project. Always / Never force it.
    [ValidateSet('Auto', 'Always', 'Never')]
    [string]$Clone = 'Auto',

    # Defaults to $env:GH_VERIFY_CLONE, then %USERPROFILE%\GHV.
    [string]$ClonePath = '',

    # Defaults to $env:UNITY_EXE, then the Unity Hub path for ProjectSettings/ProjectVersion.txt.
    [string]$UnityPath = '',

    [int]$TimeoutMinutes = 60,

    # Maximum failed tests / log lines printed.
    [int]$MaxLines = 20
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $ProjectRoot 'Logs\tests'
$script:AllPassed = $true

if (-not $ClonePath) {
    if ($env:GH_VERIFY_CLONE) { $ClonePath = $env:GH_VERIFY_CLONE } else { $ClonePath = Join-Path $env:USERPROFILE 'GHV' }
}

function Test-ProjectLocked([string]$Root) {
    $lock = Join-Path $Root 'Temp\UnityLockfile'
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    } catch {
        return $true
    }
}

function Resolve-UnityPath {
    if ($UnityPath) { return $UnityPath }
    if ($env:UNITY_EXE) { return $env:UNITY_EXE }
    $versionFile = Join-Path $ProjectRoot 'ProjectSettings\ProjectVersion.txt'
    $match = Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $match) { throw "Cannot read m_EditorVersion from $versionFile" }
    $version = $match.Matches[0].Groups[1].Value
    return "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}

function Sync-Clone([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($dir in 'Assets', 'Packages', 'ProjectSettings') {
        & robocopy (Join-Path $Source $dir) (Join-Path $Destination $dir) /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        # robocopy: 0-7 = success (files copied / extra files removed), 8+ = failure.
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $dir (exit $LASTEXITCODE)" }
    }
    $global:LASTEXITCODE = 0
}

function Invoke-Unity([string]$Project, [string[]]$Arguments, [string]$LogFile) {
    $all = @('-batchmode', '-projectPath', ('"{0}"' -f $Project), '-logFile', ('"{0}"' -f $LogFile)) + $Arguments
    $process = Start-Process -FilePath $script:Unity -ArgumentList $all -PassThru -NoNewWindow
    # Touching Handle keeps ExitCode readable after the process exits (Windows PowerShell 5.1).
    $null = $process.Handle
    if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        try { $process.Kill() } catch { }
        throw "Unity did not finish within $TimeoutMinutes min. Log: $LogFile"
    }
    return $process.ExitCode
}

function Write-LogDigest([string]$LogFile) {
    if (-not (Test-Path -LiteralPath $LogFile)) {
        Write-Host '  (no log file)'
        return
    }
    $pattern = 'error CS\d+|Scripts have compiler errors|Aborting batchmode|No valid Unity Editor license|another Unity instance|Multiple Unity instances'
    $hits = @(Select-String -LiteralPath $LogFile -Encoding UTF8 -Pattern $pattern |
        ForEach-Object { $_.Line.Trim() } | Select-Object -Unique | Select-Object -First $MaxLines)
    if ($hits.Count -gt 0) {
        $hits | ForEach-Object { Write-Host "  $_" }
    } else {
        Get-Content -LiteralPath $LogFile -Encoding UTF8 -Tail 15 | ForEach-Object { Write-Host "  $_" }
    }
}

function Get-FirstLine([System.Xml.XmlNode]$Node, [int]$Max = 200) {
    if (-not $Node) { return '' }
    $line = ($Node.InnerText.Trim() -split "`r?`n")[0]
    if ($line.Length -gt $Max) { $line = $line.Substring(0, $Max) + '...' }
    return $line
}

function Write-TestSummary([string]$Name, [string]$ResultFile, [string]$LogFile, [int]$ExitCode) {
    if (-not (Test-Path -LiteralPath $ResultFile)) {
        Write-Host "[$Name] no result XML (Unity exit $ExitCode). Log: $LogFile"
        Write-LogDigest $LogFile
        $script:AllPassed = $false
        return
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.Load($ResultFile)
    $run = $xml.SelectSingleNode('/test-run')
    Write-Host ('[{0}] {1}  total={2} passed={3} failed={4} skipped={5} inconclusive={6}  {7:N0}s  (Unity exit {8})' -f `
            $Name, $run.result, $run.total, $run.passed, $run.failed, $run.skipped, $run.inconclusive, [double]$run.duration, $ExitCode)

    $failed = @($xml.SelectNodes("//test-case[@result='Failed']"))
    foreach ($case in ($failed | Select-Object -First $MaxLines)) {
        Write-Host ('  FAIL {0} :: {1}' -f $case.fullname, (Get-FirstLine $case.SelectSingleNode('failure/message')))
    }
    if ($failed.Count -gt $MaxLines) { Write-Host "  ... $($failed.Count - $MaxLines) more failures in $ResultFile" }

    $skipped = @($xml.SelectNodes("//test-case[@result='Skipped']"))
    foreach ($case in ($skipped | Select-Object -First 5)) {
        Write-Host ('  SKIP {0} :: {1}' -f $case.fullname, (Get-FirstLine $case.SelectSingleNode('reason/message')))
    }
    if ($skipped.Count -gt 5) { Write-Host "  ... $($skipped.Count - 5) more skipped in $ResultFile" }

    if ([int]$run.failed -ne 0 -or $ExitCode -ne 0) { $script:AllPassed = $false }
}

function Invoke-TestRun([string]$Project, [string]$Name) {
    $result = Join-Path $OutDir "$Name-results.xml"
    $log = Join-Path $OutDir "$Name.log"
    Remove-Item -LiteralPath $result, $log -ErrorAction SilentlyContinue

    $arguments = @('-runTests', '-testPlatform', $Name, '-testResults', ('"{0}"' -f $result))
    # PlayMode keeps graphics (testing.md 5.2).
    if ($Name -eq 'EditMode') { $arguments += '-nographics' }
    if ($Filter) { $arguments += @('-testFilter', ('"{0}"' -f $Filter)) }

    $exit = Invoke-Unity $Project $arguments $log
    Write-TestSummary $Name $result $log $exit
}

function Invoke-CompileCheck([string]$Project) {
    $log = Join-Path $OutDir 'Compile.log'
    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue

    $exit = Invoke-Unity $Project @('-quit', '-nographics') $log
    $errors = @()
    if (Test-Path -LiteralPath $log) {
        $errors = @(Select-String -LiteralPath $log -Encoding UTF8 -Pattern 'error CS\d+' |
            ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
    }
    if ($errors.Count -eq 0 -and $exit -eq 0) {
        Write-Host '[Compile] OK (Unity exit 0)'
        return
    }

    $script:AllPassed = $false
    Write-Host "[Compile] FAILED  errors=$($errors.Count)  (Unity exit $exit). Log: $log"
    if ($errors.Count -gt 0) {
        $errors | Select-Object -First $MaxLines | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-LogDigest $log
    }
}

$script:Unity = Resolve-UnityPath
if (-not (Test-Path -LiteralPath $script:Unity)) { throw "Unity.exe not found: $script:Unity (use -UnityPath or UNITY_EXE)" }

$locked = Test-ProjectLocked $ProjectRoot
if ($Clone -eq 'Never' -and $locked) { throw 'The editor has this project open. Close it or use -Clone Auto.' }

$target = $ProjectRoot
if ($Clone -eq 'Always' -or ($Clone -eq 'Auto' -and $locked)) {
    if (Test-ProjectLocked $ClonePath) { throw "Another Unity instance has the clone open: $ClonePath" }
    Write-Host "Syncing verification clone: $ClonePath"
    Sync-Clone $ProjectRoot $ClonePath
    $target = $ClonePath
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$started = Get-Date

if ($Platform -eq 'Compile') {
    Invoke-CompileCheck $target
} else {
    $platforms = @($Platform)
    if ($Platform -eq 'All') { $platforms = @('EditMode', 'PlayMode') }
    foreach ($name in $platforms) { Invoke-TestRun $target $name }
}

Write-Host ('Done in {0:N0}s. Project: {1}' -f ((Get-Date) - $started).TotalSeconds, $target)
if ($script:AllPassed) { exit 0 } else { exit 1 }
