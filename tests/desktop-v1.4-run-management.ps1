$ErrorActionPreference = 'Stop'
$rootPath = Split-Path -Parent $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$wpfPath = Join-Path $frameworkPath 'WPF'
$runnerPath = Join-Path $PSScriptRoot 'desktop-v1.4-management-runner.exe'
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Runtime.Serialization.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $wpfPath $_) }
$resources = @(('/resource:' + (Join-Path $rootPath 'assets\xml-guide.json') + ',xml-guide.json'),('/resource:' + (Join-Path $rootPath 'assets\example.xml') + ',example.xml'))
$sources = @((Join-Path $rootPath 'src\Core.cs'),(Join-Path $rootPath 'src\MainWindow.cs'),(Join-Path $PSScriptRoot 'desktop-v1.4-management.cs'))
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:anycpu /utf8output /codepage:65001 /main:Chooser.DesktopV14Management ('/out:' + $runnerPath) @references @resources @sources
if ($LASTEXITCODE -ne 0) { throw '题库管理验证编译失败。' }
try {
  $testProcess = Start-Process -FilePath $runnerPath -ArgumentList ('"' + $rootPath + '"') -WindowStyle Hidden -Wait -PassThru
  Get-Content -LiteralPath (Join-Path $PSScriptRoot 'desktop-v1.4-management-report.txt') -Encoding UTF8
  if ($testProcess.ExitCode -ne 0) { throw '题库管理验证失败。' }
} finally { if (Test-Path -LiteralPath $runnerPath) { Remove-Item -LiteralPath $runnerPath } }
