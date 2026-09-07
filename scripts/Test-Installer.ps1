param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$testRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$testDir = [IO.Path]::GetFullPath((Join-Path $testRoot 'artifacts/install-verification'))
if (!$testDir.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test destination is outside workspace' }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8E28E5C-44C1-4B90-8EF2-705F65EAF225}_is1'
if (Test-Path $uninstallKey) { throw 'PadDim is already installed. Do not overwrite the existing installation during testing.' }
if (Test-Path -LiteralPath $testDir) { throw 'Test directory already exists. Inspect it before retrying.' }
$setupFile = Join-Path $testRoot "artifacts/installer/PadDim-Setup-$Version-win-x64.exe"
$groupName = 'PadDim'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$groupName/PadDim.lnk"
if (Test-Path -LiteralPath (Split-Path $shortcut)) { throw 'Test shortcut folder already exists' }
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'PadDim.lnk'
if (Test-Path -LiteralPath $startupShortcut) { throw 'Existing startup shortcut must not be overwritten by testing' }
try {
    $setup = Start-Process -FilePath $setupFile -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $testDir + '"'),('/GROUP="' + $groupName + '"'),('/LOG="' + (Join-Path $testRoot 'artifacts/installer-test.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Install failed: $($setup.ExitCode)" }
    foreach ($file in @('PadDim.exe','coreclr.dll','LICENSE.txt','licenses/microsoft.netcore.app.runtime.win-x64-LICENSE.TXT','THIRD-PARTY-NOTICES.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $testDir $file))) { throw "Missing installed file: $file" }
    }
    if (!(Test-Path $uninstallKey)) { throw 'Uninstall registration missing' }
    if (!(Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut missing' }
    Write-Output 'PASS installation, bundled runtime, notices, Start menu, uninstall registration'
    if (Test-Path -LiteralPath $startupShortcut) { throw 'Startup must be opt-in' }
    $baseArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $testDir + '"'))
    $optIn = Start-Process -FilePath $setupFile -ArgumentList ($baseArguments + '/TASKS=startup') -WindowStyle Hidden -Wait -PassThru
    if ($optIn.ExitCode -ne 0 -or !(Test-Path -LiteralPath $startupShortcut)) { throw 'Startup opt-in failed' }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($startupShortcut)
    if ($link.TargetPath -ne (Join-Path $testDir 'PadDim.exe') -or $link.Arguments -ne '--tray') { throw 'Startup shortcut target or arguments incorrect' }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    $optOut = Start-Process -FilePath $setupFile -ArgumentList ($baseArguments + '/TASKS=') -WindowStyle Hidden -Wait -PassThru
    if ($optOut.ExitCode -ne 0 -or (Test-Path -LiteralPath $startupShortcut)) { throw 'Startup opt-out failed' }
    $optIn = Start-Process -FilePath $setupFile -ArgumentList ($baseArguments + '/TASKS=startup') -WindowStyle Hidden -Wait -PassThru
    if ($optIn.ExitCode -ne 0 -or !(Test-Path -LiteralPath $startupShortcut)) { throw 'Startup re-enable failed' }
    Write-Output 'PASS startup opt-in, --tray arguments, and opt-out on upgrade'
    $testResult = Join-Path $testRoot 'artifacts/installed-input-smoke.txt'
    $app = Start-Process -FilePath (Join-Path $testDir 'PadDim.exe') -ArgumentList @('--smoke-test',('"' + $testResult + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($app.ExitCode -ne 0 -or !(Test-Path -LiteralPath $testResult)) { throw 'Installed executable smoke test failed' }
    Write-Output 'PASS installed executable launch and input API initialization'
    Get-Content -LiteralPath $testResult
    $trayResult = Join-Path $testRoot 'artifacts/installed-tray-smoke.txt'
    $app = Start-Process -FilePath (Join-Path $testDir 'PadDim.exe') -ArgumentList @('--tray-smoke-test',('"' + $trayResult + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($app.ExitCode -ne 0) { throw 'Tray startup test failed' }
    Get-Content -LiteralPath $trayResult
} finally {
    $uninstaller = Join-Path $testDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        # This uninstaller was just installed into the verified workspace target above.
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed: $($uninstall.ExitCode)" }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while ((Test-Path -LiteralPath $uninstaller) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
        if ((Test-Path $uninstallKey) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $startupShortcut) -or (Test-Path -LiteralPath (Join-Path $testDir 'PadDim.exe'))) { throw 'Uninstall left registered app, startup shortcut, or executable behind' }
        Write-Output 'PASS uninstall removed app, shortcut, and registration'
    }
}
