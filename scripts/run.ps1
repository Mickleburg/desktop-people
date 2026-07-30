$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Get-DesktopPeopleDotNet
& $dotnet run --project (Join-Path $PSScriptRoot '..\src\DesktopPeople.App\DesktopPeople.App.csproj') -c Debug

