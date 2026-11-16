param([string]$NsisCompiler, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'; $env:PYTHONIOENCODING = 'utf-8'; $env:CI = 'true'
Set-Location $PSScriptRoot
if (!$SkipBuild) { & "$PSScriptRoot\build.ps1" -Test -UiTest }
$version = ([version][System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PSScriptRoot 'bin\SaruTask.exe')).FileVersion).ToString(3)
$output = Join-Path $PSScriptRoot 'artifacts\release'
$payload = Join-Path $output "SaruTask-$version-win"
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\SaruTask.exe'),(Join-Path $PSScriptRoot 'README.md') -Destination $payload -Force
$zipName = "SaruTask-$version-win.zip"
Compress-Archive -LiteralPath (Join-Path $payload 'SaruTask.exe'),(Join-Path $payload 'README.md') -DestinationPath (Join-Path $output $zipName) -Force
function ConvertTo-WslPath([string]$Value) {
    $absolute = [IO.Path]::GetFullPath($Value)
    if ($absolute -notmatch '^([A-Za-z]):\\(.*)$') { throw 'A Windows drive path is required.' }
    return '/mnt/' + $Matches[1].ToLowerInvariant() + '/' + $Matches[2].Replace('\','/')
}
function Quote-Bash([string]$Value) { return "'" + $Value.Replace("'", "'\''") + "'" }
$rootWsl = ConvertTo-WslPath $PSScriptRoot
$compiler = 'makensis'
$prefix = ''
if ($NsisCompiler) {
    $compiler = Quote-Bash (ConvertTo-WslPath $NsisCompiler)
    $nsisDir = [IO.Path]::GetFullPath((Join-Path (Split-Path $NsisCompiler) '..\share\nsis'))
    $prefix = 'NSISDIR=' + (Quote-Bash (ConvertTo-WslPath $nsisDir)) + ' '
}
$arguments = @('-V3','-WX',"-DPRODUCT_VERSION=$version","-DPAYLOAD_DIR=$(ConvertTo-WslPath $payload)","-DOUTPUT_FILE=$(ConvertTo-WslPath (Join-Path $output "SaruTask-$version-Setup.exe"))","-DICON_FILE=$(ConvertTo-WslPath (Join-Path $PSScriptRoot 'obj\SaruTask.ico'))",'installer/SaruTask.nsi') | ForEach-Object { Quote-Bash $_ }
$command = 'cd ' + (Quote-Bash $rootWsl) + ' && ' + $prefix + $compiler + ' ' + ($arguments -join ' ')
& wsl.exe -d Ubuntu-24.04-WSL1 bash -lc $command
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$names = @($zipName,"SaruTask-$version-Setup.exe")
$checksums = @($names | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_ })
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$checksums,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output ($zipName + '.sha256')),$checksums[0] + "`n",[Text.UTF8Encoding]::new($false))
Get-ChildItem -LiteralPath $output -File | Select-Object Name,Length
