param([switch]$FrameworkDependent, [switch]$SkipVerification, [ValidatePattern('^[a-z0-9-]+$')][string]$ReleaseName = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build CSharpEndpointRag.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    if (-not $SkipVerification) {
        dotnet run --project tests/EndpointRag.Verification -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
    }
    $publishRoot = Join-Path $PSScriptRoot ('artifacts\release\' + $ReleaseName)
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    dotnet publish src/EndpointRag.WinForms/EndpointRag.WinForms.csproj -c Release -r win-x64 --self-contained $selfContained -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $pdbFiles = @(Get-ChildItem -LiteralPath $publishRoot -Filter '*.pdb' -Recurse)
    if ($pdbFiles.Count -gt 0) { throw 'Unexpected PDB files in release output.' }
    $archivePath = Join-Path $PSScriptRoot ('artifacts\release\CSharpEndpointRag-' + $ReleaseName + '.zip')
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archivePath -Force
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText($archivePath + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($archivePath) + [Environment]::NewLine)
    Write-Host ('READY: ' + (Join-Path $publishRoot 'CSharpEndpointRag.exe'))
    Write-Host ('ZIP: ' + $archivePath)
}
finally { Pop-Location }
