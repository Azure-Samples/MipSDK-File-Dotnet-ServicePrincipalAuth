param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory
)

$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (-not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'Published MIP smoke checks require PowerShell 7 on Windows x64.'
}

foreach ($relativePath in @(
    'MipSdkDotNetQuickstart.exe',
    'Microsoft.InformationProtection.dll',
    'appsettings.json',
    'x64\mip_dotnet.dll',
    'x64\mip_file_sdk.dll',
    'x64\mip_protection_sdk.dll',
    'x64\mip_upe_sdk.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $directory $relativePath) -PathType Leaf)) {
        throw "Missing published dependency: $relativePath"
    }
}

if (Test-Path -LiteralPath (Join-Path $directory 'appsettings.local.json')) {
    throw 'Local configuration must not be published.'
}
$settings = Get-Content -LiteralPath (Join-Path $directory 'appsettings.json') -Raw | ConvertFrom-Json
if ($settings.Ida.ClientId -or $settings.Ida.Tenant -or $settings.Ida.ClientSecret -or $settings.Ida.CertThumbprint) {
    throw 'Published sample defaults must not contain application credentials or tenant identifiers.'
}
if ($settings.App.AcceptServiceConsent) {
    throw 'Published defaults must require explicit service consent.'
}

& (Join-Path $directory 'MipSdkDotNetQuickstart.exe') --help
if ($LASTEXITCODE -ne 0) {
    throw 'Published CLI help failed.'
}

Add-Type -Path (Join-Path $directory 'Microsoft.InformationProtection.dll')
[Microsoft.InformationProtection.MIP]::Initialize(
    [Microsoft.InformationProtection.MipComponent]::File,
    (Join-Path $directory 'x64'))
Write-Output 'Published Windows x64 CLI, configuration, and native SDK initialization passed.'
