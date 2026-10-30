param(
    [string]$Go = 'go',
    [string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$oldCgo = $env:CGO_ENABLED
$oldGoos = $env:GOOS
$oldGoarch = $env:GOARCH
Push-Location -LiteralPath $sourceDirectory
try {
    $env:CGO_ENABLED = '0'
    $env:GOOS = ''
    $env:GOARCH = ''
    & $Go test ./...
    if ($LASTEXITCODE -ne 0) { throw 'Go HTTP 服务测试失败。' }
    foreach ($target in @(
        @{ OS = 'windows'; Arch = 'amd64'; Name = '选择器Go服务端v1.4-Windows.exe' },
        @{ OS = 'linux'; Arch = 'amd64'; Name = '选择器Go服务端v1.4-Linux-amd64' }
    )) {
        $env:GOOS = $target.OS
        $env:GOARCH = $target.Arch
        $destination = Join-Path $OutputDirectory $target.Name
        & $Go build -trimpath -ldflags '-s -w' -o $destination .
        if ($LASTEXITCODE -ne 0) { throw ('Go 编译失败：' + $target.OS) }
        Get-Item -LiteralPath $destination | Select-Object FullName, Length, LastWriteTime
    }
}
finally {
    $env:CGO_ENABLED = $oldCgo
    $env:GOOS = $oldGoos
    $env:GOARCH = $oldGoarch
    Pop-Location
}
