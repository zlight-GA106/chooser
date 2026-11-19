$ErrorActionPreference = 'Stop'
$rootPath = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$scratch = Join-Path $PSScriptRoot ('csv-test-data-' + [Guid]::NewGuid().ToString('N'))
$runner = Join-Path $scratch 'csv-core.exe'
$fixture = Join-Path $scratch 'fixture.csv'
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    & $compiler /nologo /target:exe /utf8output /codepage:65001 ('/out:' + $runner) `
        /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Runtime.Serialization.dll `
        (Join-Path $rootPath 'src\Core.cs') (Join-Path $rootPath 'src\CsvQuiz.cs') (Join-Path $PSScriptRoot 'csv-core.cs')
    if ($LASTEXITCODE -ne 0) { throw 'CSV 核心验证编译失败。' }
    & $runner $fixture (Join-Path $rootPath 'assets\example.csv')
    if ($LASTEXITCODE -ne 0) { throw 'CSV 核心验证失败。' }
} finally {
    if (Test-Path -LiteralPath $runner) { Remove-Item -LiteralPath $runner }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
    Remove-Item -LiteralPath $scratch
}
