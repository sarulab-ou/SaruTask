param([switch]$Test, [switch]$UiTest)
$ErrorActionPreference = 'Stop'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:CI = 'true'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
New-Item -ItemType Directory -Path bin -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath src -Filter '*.cs' | ForEach-Object { $_.FullName })
& "$PSScriptRoot\scripts\Generate-Icon.ps1"
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Runtime.Serialization.dll', '/reference:System.Net.Http.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
& $compiler /nologo /utf8output /target:winexe /optimize+ /platform:anycpu /out:bin\SaruTask.exe /win32manifest:app.manifest /win32icon:obj\SaruTask.ico @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Built bin\SaruTask.exe'
if ($Test) {
    & $compiler /nologo /utf8output /target:exe /out:bin\SaruTask.Tests.exe /reference:System.Runtime.Serialization.dll src\SearchCatalog.cs src\AppSettings.cs tests\SearchCatalogTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & .\bin\SaruTask.Tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $compiler /nologo /utf8output /target:exe /out:bin\SaruTask.RenameTests.exe src\RenameService.cs tests\RenameServiceTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Rename test compilation failed.' }
    & .\bin\SaruTask.RenameTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Rename tests failed.' }
    & $compiler /nologo /utf8output /target:exe /out:bin\SaruTask.UpdateTests.exe /reference:System.Runtime.Serialization.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:Microsoft.CSharp.dll src\UpdateService.cs src\AppSettings.cs tests\UpdateTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Update test compilation failed.' }
    & .\bin\SaruTask.UpdateTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Update tests failed.' }
}
if ($UiTest) {
    & $compiler /nologo /utf8output /target:exe /out:bin\SaruTask.UiTests.exe /main:MainFormTests @references @sources tests\MainFormTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    & .\bin\SaruTask.UiTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
}
