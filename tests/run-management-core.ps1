$ErrorActionPreference = 'Stop'
$testDirectory = $PSScriptRoot
$rootDirectory = Split-Path -Parent $testDirectory
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$runnerPath = Join-Path $testDirectory 'management-core-runner.exe'
& $compilerPath /nologo /target:exe /utf8output /codepage:65001 /main:Chooser.ManagementCoreTests ('/out:' + $runnerPath) /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Runtime.Serialization.dll (Join-Path $rootDirectory 'src\Core.cs') (Join-Path $testDirectory 'management-core.cs')
if ($LASTEXITCODE -ne 0) { throw '题库管理核心验证编译失败。' }
try {
    $runner = Start-Process -FilePath $runnerPath -ArgumentList ('"' + $rootDirectory + '"') -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $testDirectory 'management-core-report.txt') -Encoding UTF8
    if ($runner.ExitCode -ne 0) { throw '题库管理核心验证失败。' }
} finally { if (Test-Path -LiteralPath $runnerPath) { Remove-Item -LiteralPath $runnerPath } }
