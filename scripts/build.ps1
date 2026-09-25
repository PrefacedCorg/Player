# 确保 libmpv 就绪后编译
#
# 先拿 third_party/mpv/manifest.json 里的「7z 文件名 + dll 哈希」跟当前状态比对：
#   一致 → dll 没变过，跳过解压直接编译
#   不一致（7z 换过、dll 缺失或被改动）→ 重新解压，再编译
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Configuration Debug
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$Variant = 'x86_64-v3',

    # 跳过还原（已还原过时可省几秒）
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mpvDir = Join-Path $root 'third_party\mpv'
$manifestPath = Join-Path $mpvDir 'manifest.json'
$dll = Join-Path $mpvDir 'win-x64\libmpv-2.dll'

# 当前仓库里的 7z（变体可能有多版本，取字典序最后一个即最新）
$archive = Get-ChildItem -Path $mpvDir -Filter "mpv-dev-$Variant-*.7z" -File -ErrorAction SilentlyContinue |
    Sort-Object Name | Select-Object -Last 1

$reason = $null
if (-not $archive) {
    $reason = "找不到 mpv-dev-$Variant-*.7z"
}
elseif (-not (Test-Path $manifestPath)) {
    $reason = '缺少 manifest.json'
}
elseif (-not (Test-Path $dll)) {
    $reason = 'dll 尚未解压'
}
else {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $actual = (Get-FileHash -Path $dll -Algorithm SHA256).Hash

    if ($manifest.archive -ne $archive.Name) {
        $reason = "7z 已更换：记录 $($manifest.archive)，实际 $($archive.Name)"
    }
    elseif ($manifest.sha256 -ne $actual) {
        $reason = 'dll 哈希与记录不一致'
    }
}

if ($reason) {
    Write-Host "需要解压（$reason）"
    & (Join-Path $PSScriptRoot 'extract-libmpv.ps1') -Variant $Variant -Force

    # 解压完把清单补上（记录当前 7z 与它解出的 dll 哈希），下次就能直接跳过解压。
    # fetch-libmpv.ps1 也会写这份清单，这里兜底，保证本地始终有一份可用的。
    if ($archive -and (Test-Path $dll)) {
        $refreshed = [ordered]@{
            archive = $archive.Name
            variant = $Variant
            dll     = 'libmpv-2.dll'
            size    = (Get-Item $dll).Length
            sha256  = (Get-FileHash -Path $dll -Algorithm SHA256).Hash
        }
        $refreshed | ConvertTo-Json | Set-Content -Path $manifestPath
        Write-Host "已更新清单 $manifestPath -> $($archive.Name)"
    }
}
else {
    Write-Host "libmpv 未变化（$($archive.Name)），跳过解压。"
}

Push-Location $root
try {
    if (-not $SkipRestore) {
        dotnet restore Player.slnx
        if ($LASTEXITCODE -ne 0) { throw '还原失败。' }
    }

    dotnet build Player.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
}
finally {
    Pop-Location
}
