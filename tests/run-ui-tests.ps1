$ErrorActionPreference = 'Stop'
$testPath = $PSScriptRoot
$rootPath = Split-Path -Parent $testPath
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$wpfPath = Join-Path $frameworkPath 'WPF'
$runnerPath = Join-Path $testPath 'UiIntegration.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Runtime.Serialization.dll', 'System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfPath $_) }
$sourceFiles = @((Join-Path $rootPath 'src\Core.cs'), (Join-Path $rootPath 'src\MainWindow.cs'), (Join-Path $testPath 'UiIntegration.cs'))
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:anycpu /utf8output /codepage:65001 /main:Chooser.UiIntegration ('/out:' + $runnerPath) @references @sourceFiles
if ($LASTEXITCODE -ne 0) { throw '界面验证程序编译失败。' }
try {
    $testProcess = Start-Process -FilePath $runnerPath -ArgumentList ('"' + $rootPath + '"') -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $testPath 'ui-integration-console.txt') -RedirectStandardError (Join-Path $testPath 'ui-integration-errors.txt')
    $reportPath = Join-Path $testPath 'ui-integration-report.txt'
    if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath -Encoding UTF8 }
    if ($testProcess.ExitCode -ne 0) { throw ('界面验证失败，退出码：' + $testProcess.ExitCode) }
}
finally {
    if (Test-Path -LiteralPath $runnerPath) { Remove-Item -LiteralPath $runnerPath }
}
