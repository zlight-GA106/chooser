$ErrorActionPreference = 'Stop'
$rootPath = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$wpfPath = Join-Path $frameworkPath 'WPF'
$outputPath = Join-Path $rootPath '选择器单文件版v1.4.exe'
$sourceFiles = @('src\Program.cs', 'src\Core.cs', 'src\MainWindow.cs', 'src\SelfTests.cs') | ForEach-Object { Join-Path $rootPath $_ }
$compilerArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/codepage:65001', '/main:Chooser.Program',
    ('/out:' + $outputPath),
    ('/win32manifest:' + (Join-Path $rootPath 'src\app.manifest')),
    ('/reference:' + (Join-Path $frameworkPath 'System.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Core.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Xml.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Xml.Linq.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Runtime.Serialization.dll')),
    ('/reference:' + (Join-Path $wpfPath 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpfPath 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $wpfPath 'WindowsBase.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Xaml.dll')),
    ('/resource:' + (Join-Path $rootPath 'assets\default.xml') + ',default.xml'),
    ('/resource:' + (Join-Path $rootPath 'assets\xml-guide.json') + ',xml-guide.json'),
    ('/resource:' + (Join-Path $rootPath 'assets\example.xml') + ',example.xml')
)
$iconPath = Join-Path $rootPath 'assets\chooser.ico'
if (Test-Path -LiteralPath $iconPath) { $compilerArguments += ('/win32icon:' + $iconPath) }
& $compilerPath @compilerArguments @sourceFiles
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Get-Item -LiteralPath $outputPath | Select-Object FullName, Length, LastWriteTime
