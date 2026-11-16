param([string]$Version = '0.1.1')
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'; $env:PYTHONIOENCODING = 'utf-8'; $env:CI = 'true'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release = Join-Path $root 'artifacts\release'
$target = [IO.Path]::GetFullPath((Join-Path $root ('artifacts\installer-test-' + [Guid]::NewGuid().ToString('N'))))
if (!$target.StartsWith($root + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Test directory escaped workspace.' }
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask'
$appKey = 'HKCU:\Software\SaruTask'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'SaruTask\SaruTask.lnk'
if ((Test-Path $registration) -or (Test-Path $appKey) -or (Test-Path -LiteralPath $shortcut)) { throw 'An existing SaruTask installation is present; run this test in a clean Windows user profile.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $release "SaruTask-$Version-win.zip"
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    if (($entries -join ',') -ne 'README.md,SaruTask.exe') { throw 'Unexpected ZIP entries.' }
} finally { $archive.Dispose() }
$payload = Join-Path $release "SaruTask-$Version-win\SaruTask.exe"
$expectedHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
New-Item -ItemType Directory -Path $target -Force | Out-Null
$settings = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SaruTask\settings.json'
$settingsHash = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash } else { $null }
$installer = Join-Path $release "SaruTask-$Version-Setup.exe"
$setup = Start-Process -FilePath $installer -ArgumentList @('/S',"/D=$target") -PassThru -Wait -WindowStyle Hidden
if ($setup.ExitCode -ne 0) { throw "Installer failed: $($setup.ExitCode)" }
if ((Get-FileHash -LiteralPath (Join-Path $target 'SaruTask.exe') -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Installed EXE differs from ZIP.' }
if ((Get-ItemProperty $registration).DisplayVersion -ne $Version) { throw 'Uninstall registration has the wrong version.' }
if (!(Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut was not created.' }
Write-Host 'PASS installer, ZIP and uninstall registration agree'
$sentinel = Join-Path $target 'keep-user-data.txt'
[IO.File]::WriteAllText($sentinel,'preserve this unrelated file')
$uninstall = Start-Process -FilePath (Join-Path $target 'Uninstall.exe') -ArgumentList '/S' -PassThru -Wait -WindowStyle Hidden
if ($uninstall.ExitCode -ne 0) { throw "Uninstaller failed: $($uninstall.ExitCode)" }
$timeout = [Diagnostics.Stopwatch]::StartNew()
while ((Test-Path -LiteralPath (Join-Path $target 'SaruTask.exe')) -or (Test-Path $registration)) {
    if ($timeout.ElapsedMilliseconds -gt 10000) { throw 'Uninstaller did not finish.' }
    Start-Sleep -Milliseconds 100
}
if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstaller deleted unrelated data.' }
if ((Test-Path $appKey) -or (Test-Path -LiteralPath $shortcut)) { throw 'Installation registration or shortcut remains.' }
$afterHash = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash } else { $null }
if ($settingsHash -ne $afterHash) { throw 'User settings changed during installation/uninstallation.' }
Write-Host 'PASS uninstall preserves unrelated data and settings'
Remove-Item -LiteralPath $sentinel
if (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) { Remove-Item -LiteralPath $target }
foreach ($line in [IO.File]::ReadAllLines((Join-Path $release 'SHA256SUMS.txt'))) {
    $parts = $line -split '  ',2
    if ($parts.Count -ne 2 -or (Get-FileHash -LiteralPath (Join-Path $release $parts[1]) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $parts[0]) { throw 'Release checksum mismatch.' }
}
Write-Host 'PASS release checksums match both distribution packages'
