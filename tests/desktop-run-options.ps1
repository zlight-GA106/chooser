$ErrorActionPreference = 'Stop'
$testPath = $PSScriptRoot
$rootPath = Split-Path -Parent $testPath
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$wpfPath = Join-Path $frameworkPath 'WPF'
$runnerPath = Join-Path $testPath 'desktop-options-runner.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfPath $_) }
$sources = @((Join-Path $rootPath 'src\Core.cs'), (Join-Path $rootPath 'src\MainWindow.cs'), (Join-Path $testPath 'desktop-options.cs'))
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:anycpu /utf8output /codepage:65001 /main:Chooser.DesktopOptions ('/out:' + $runnerPath) @references @sources
if ($LASTEXITCODE -ne 0) { throw '选项验证程序编译失败。' }
try {
    $testProcess = Start-Process -FilePath $runnerPath -ArgumentList ('"' + $rootPath + '"') -WindowStyle Hidden -PassThru -Wait
    Get-Content -LiteralPath (Join-Path $testPath 'desktop-options-report.txt') -Encoding UTF8
    if ($testProcess.ExitCode -ne 0) { throw ('选项验证失败：' + $testProcess.ExitCode) }
}
finally { if (Test-Path -LiteralPath $runnerPath) { Remove-Item -LiteralPath $runnerPath } }
