$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Get-DesktopPeopleDotNet
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
$project = Join-Path $PSScriptRoot '..\src\DesktopPeople.App\DesktopPeople.App.csproj'
$nugetConfig = Join-Path $PSScriptRoot '..\NuGet.Config'
& $dotnet restore $project -r win-x64 --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet publish `
    $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Output "Published: $(Join-Path $output 'DesktopPeople.exe')"
