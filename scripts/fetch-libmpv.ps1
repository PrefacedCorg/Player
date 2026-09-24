# 获取 libmpv 原生库（本机缓存，不入库：约 120MB）
# 用法：powershell -ExecutionPolicy Bypass -File scripts/fetch-libmpv.ps1
# 说明：libmpv 不通过 NuGet 分发，官方也无预编译包，这里取 LibMpv 项目维护的 Windows x64 构建。

[CmdletBinding()]
param(
    # 注意：raw.githubusercontent.com 返回的是 Git LFS 指针文件，必须用 media 端点拿真实内容
    [string]$Source = 'https://media.githubusercontent.com/media/mysteryx93/LibMpv-OpenGL/main/MpvDll/win-x64/libmpv-2.dll',
    [string]$TargetDir = (Join-Path $PSScriptRoot '..\third_party\mpv\win-x64')
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $TargetDir 'libmpv-2.dll'
$minSize = 10MB

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

if (Test-Path $target) {
    $existing = (Get-Item $target).Length
    if ($existing -gt $minSize) {
        Write-Host "已存在 libmpv-2.dll（$([math]::Round($existing / 1MB, 1)) MB），跳过下载。"
        return
    }
    Write-Host "已存在的文件过小（$existing 字节），疑似 LFS 指针文件，重新下载。"
}

Write-Host "下载 libmpv-2.dll ..."
Invoke-WebRequest -Uri $Source -OutFile $target -TimeoutSec 600

$size = (Get-Item $target).Length
if ($size -le $minSize) {
    Remove-Item $target -Force
    throw "下载结果异常（$size 字节），可能是 Git LFS 指针文件。请检查下载地址。"
}

Write-Host "完成：$target（$([math]::Round($size / 1MB, 1)) MB）"
Write-Host "构建 Player.App 时会自动拷贝到输出目录（见 Player.App.csproj）。"