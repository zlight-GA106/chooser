$ErrorActionPreference = 'Stop'
$rootPath = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$runner = Join-Path $env:TEMP ('chooser-portable-tests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /utf8output /codepage:65001 ('/out:' + $runner) `
        /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll /reference:System.Runtime.Serialization.dll `
        (Join-Path $rootPath 'src\Core.cs') (Join-Path $rootPath 'src\PortableStore.cs') (Join-Path $PSScriptRoot 'portable-store.cs')
    if ($LASTEXITCODE -ne 0) { throw '便携存储测试编译失败。' }
    & $runner
    if ($LASTEXITCODE -ne 0) { throw '便携存储测试失败。' }
} finally {
    if (Test-Path -LiteralPath $runner) { Remove-Item -LiteralPath $runner }
}
