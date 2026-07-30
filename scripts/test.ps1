$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Get-DesktopPeopleDotNet
$project = Join-Path $PSScriptRoot '..\tests\DesktopPeople.Tests\DesktopPeople.Tests.csproj'
$nugetConfig = Join-Path $PSScriptRoot '..\NuGet.Config'
& $dotnet restore $project --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet run --project $project -c Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
