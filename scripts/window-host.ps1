$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Get-DesktopPeopleDotNet
$project = Join-Path $PSScriptRoot '..\tests\DesktopPeople.WindowHost\DesktopPeople.WindowHost.csproj'
$nugetConfig = Join-Path $PSScriptRoot '..\NuGet.Config'
& $dotnet restore $project --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet build $project -c Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$executable = Join-Path $PSScriptRoot '..\tests\DesktopPeople.WindowHost\bin\Debug\net10.0-windows\DesktopPeople.WindowHost.exe'
Start-Process -FilePath $executable
