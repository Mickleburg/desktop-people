# Exports the Godot host as a standalone Windows build, the counterpart of publish.ps1 for the
# WinForms one.
#
# Requires the Godot editor binary (GODOT env var, or C:\Godot\godot.exe) and its export
# templates for the matching version. The editor cannot export without them, and installing
# them is a ~1.1 GB download (editor: Project -> Manage Export Templates).
#
# The exported result is an .exe PLUS a data_* folder holding the .NET assemblies. Unlike the
# WinForms artifact it is not a single file, and both parts must be shipped together.
#
# Kept ASCII-only on purpose: PowerShell 5.1 reads .ps1 files as ANSI, and a stray em dash in a
# comment was enough to break parsing of the rest of the file.
$ErrorActionPreference = 'Stop'

$godot = if ($env:GODOT) { $env:GODOT } else { 'C:\Godot\godot.exe' }
if (-not (Test-Path $godot)) {
    throw "Godot editor not found at '$godot'. Set the GODOT environment variable to its path."
}

# Godot's .NET export shells out to `dotnet`, which is not always on PATH here.
if (Test-Path 'C:\Program Files\dotnet') {
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
}

# Resolved for readable messages only. Godot itself accepts a path with '..' in it just fine;
# that was measured, after an earlier version of this comment claimed otherwise.
$projectDir = (Resolve-Path (Join-Path $PSScriptRoot '..\src\DesktopPeople.Godot')).Path
$output = (Resolve-Path (Join-Path $PSScriptRoot '..\artifacts')).Path
$output = Join-Path $output 'godot'
New-Item -ItemType Directory -Force $output | Out-Null
$exe = Join-Path $output 'DesktopPeople.exe'
$before = if (Test-Path $exe) { (Get-Item $exe).LastWriteTimeUtc } else { [datetime]::MinValue }

# Two things here are load-bearing, both established by measurement rather than by reasoning:
#
# 1. The project is named with --path instead of changing directory. Push-Location moves
#    PowerShell's own location but leaves [Environment]::CurrentDirectory alone, and a native
#    process inherits the latter, so Godot would start in the repository root and find no
#    project.godot there.
# 2. The output is captured. Left to stream, the command produced nothing at all and exported
#    nothing, with no error to show for it; captured, the same call works. Do not "simplify"
#    this back into a bare invocation.
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$exportOutput = & $godot --headless --path $projectDir --export-release 'Windows Desktop' 2>&1 | Out-String
$ErrorActionPreference = $previous
Write-Output $exportOutput

# Success is judged by the artifact rather than by an exit code. Godot writes its progress to
# stderr, and in this PowerShell $LASTEXITCODE did not survive the call to be read afterwards:
# a perfectly good export then reported failure with a blank code. Checking that the executable
# exists and is newer than before answers the question that actually matters.
if (-not (Test-Path $exe)) {
    throw "Godot export produced no executable at '$exe'."
}

if ((Get-Item $exe).LastWriteTimeUtc -le $before) {
    throw "Godot export left '$exe' untouched, so it did not run. See its output above."
}

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$payload = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Output "Exported: $((Resolve-Path $exe).Path)"
Write-Output "SHA256:   $hash"
Write-Output "Payload:  $([math]::Round($payload / 1MB, 1)) MB (executable plus its data folder)"
