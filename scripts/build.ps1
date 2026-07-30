$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Get-DesktopPeopleDotNet
$solution = Join-Path $PSScriptRoot '..\DesktopPeople.slnx'
$nugetConfig = Join-Path $PSScriptRoot '..\NuGet.Config'
& $dotnet restore $solution --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet build $solution -c Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
