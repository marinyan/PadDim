param([string]$IsccPath = '', [string]$Version = '0.1.8')
$ErrorActionPreference = 'Stop'
$buildRoot = Split-Path $PSScriptRoot -Parent
Push-Location $buildRoot
try {
    if (!$IsccPath) {
        $candidates = @('artifacts/tools/inno/ISCC.exe', "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 7/ISCC.exe")
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$IsccPath) { throw 'Inno Setup の ISCC.exe を -IsccPath で指定してください。https://jrsoftware.org/isdl.php' }
    }
    & "$PSScriptRoot/New-Icon.ps1"
    dotnet restore src/PadDim/PadDim.csproj --configfile NuGet.Config -r win-x64 -p:SelfContained=true
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet publish src/PadDim/PadDim.csproj --no-restore -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o artifacts/publish/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    $licenseDir = 'artifacts/publish/win-x64/licenses'
    New-Item -ItemType Directory -Force $licenseDir | Out-Null
    $runtimeConfig = Get-Content 'artifacts/publish/win-x64/PadDim.runtimeconfig.json' -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $packageDir = Join-Path 'artifacts/packages' "$packageName/$($framework.version)"
        Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match 'LICENSE|THIRD-PARTY' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseDir ($packageName + '-' + $_.Name))
        }
    }
    Copy-Item -LiteralPath 'artifacts/packages/system.management/10.0.0/THIRD-PARTY-NOTICES.TXT' -Destination "$licenseDir/System.Management-THIRD-PARTY-NOTICES.TXT"
    Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.txt' -Destination 'artifacts/publish/win-x64/THIRD-PARTY-NOTICES.txt'
    Copy-Item -LiteralPath 'LICENSE.txt' -Destination 'artifacts/publish/win-x64/LICENSE.txt'
    & $IsccPath /Qp "/DAppVersion=$Version" installer/PadDim.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    Get-Item "artifacts/installer/PadDim-Setup-$Version-win-x64.exe"
} finally { Pop-Location }
