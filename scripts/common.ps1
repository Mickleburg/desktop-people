function Get-DesktopPeopleDotNet {
    $toolRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\.tools')).Path
    $env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $toolRoot 'nuget-packages'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'

    $localSdk = Join-Path $PSScriptRoot '..\.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) {
        return (Resolve-Path -LiteralPath $localSdk).Path
    }

    $systemSdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $systemSdk) {
        throw 'The .NET 10 SDK is required. See README.md for setup instructions.'
    }

    return $systemSdk.Source
}
