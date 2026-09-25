# 更新入库的 libmpv 7z 包，并生成清单文件
#
# 源：shinchiro/mpv-winbuild-cmake 最新 release 里的 mpv-dev-<变体> 7z 包
# （官方预编译构建，libmpv-2.dll 就在 7z 根目录；包本身约 32MB，直接入库，
#   dll 由解压得到，不入库）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/fetch-libmpv.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/fetch-libmpv.ps1 -Variant x86_64
#   powershell -ExecutionPolicy Bypass -File scripts/fetch-libmpv.ps1 -Force
#
# 产出：
#   third_party/mpv/mpv-dev-<变体>-<日期>-git-<commit>.7z
#   third_party/mpv/manifest.json   记录 7z 文件名与 dll 哈希，供 build.ps1 判断是否需要重新解压

[CmdletBinding()]
param(
    # 构建变体。v3 针对支持 AVX2 的 64 位 CPU；老机器改用 x86_64（通用 64 位）
    [string]$Variant = 'x86_64-v3',

    [string]$PackageDir = (Join-Path $PSScriptRoot '..\third_party\mpv'),

    # 即使同名包已存在也重新下载
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = 'shinchiro/mpv-winbuild-cmake'

New-Item -ItemType Directory -Force -Path $PackageDir | Out-Null

Write-Host "查询 $repo 最新 release ..."
$headers = @{ 'User-Agent' = 'Player-fetch-libmpv'; 'Accept' = 'application/vnd.github+json' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }

$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" `
    -Headers $headers -TimeoutSec 120

$pattern = '^mpv-dev-' + [regex]::Escape($Variant) + '-\d{8}-git-[0-9a-f]+\.7z$'
$asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
if (-not $asset) {
    throw "release $($release.tag_name) 里没有匹配 $pattern 的资产（变体：$Variant）。"
}

$target = Join-Path $PackageDir $asset.name
$downloaded = $false

if ((Test-Path $target) -and -not $Force) {
    Write-Host "已存在 $($asset.name)，跳过下载。强制更新请加 -Force。"
}
else {
    Write-Host "版本 $($release.tag_name)，资产 $($asset.name)（$([math]::Round($asset.size / 1MB, 1)) MB）"

    # 先下到临时目录，成功后再替换，避免中断留下半个包
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('libmpv-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    $staged = Join-Path $temp $asset.name

    try {
        Write-Host "下载 $($asset.browser_download_url) ..."
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $staged -TimeoutSec 900

        $size = (Get-Item $staged).Length
        if ($size -le 1MB) { throw "下载结果异常（$size 字节），疑似重定向页或 LFS 指针。" }
        if ($size -ne $asset.size) { Write-Warning "大小与 release 记录不一致（$size / $($asset.size)）。" }

        # 清掉同变体的旧版本，仓库里只留一个
        Get-ChildItem -Path $PackageDir -Filter "mpv-dev-$Variant-*.7z" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne $asset.name } |
            ForEach-Object {
                Write-Host "移除旧包 $($_.Name)"
                Remove-Item $_.FullName -Force
            }

        Move-Item -Path $staged -Destination $target -Force
        Write-Host "已入库：$target（$([math]::Round($size / 1MB, 1)) MB）"
    }
    finally {
        Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $downloaded = $true
}

# 把 dll 解出来。刚下载过就强制重解，否则交给脚本按时间戳判断
$extractArgs = @{ Variant = $Variant }
if ($downloaded) { $extractArgs['Force'] = $true }
& (Join-Path $PSScriptRoot 'extract-libmpv.ps1') @extractArgs

# 写清单：7z 文件名 + dll 哈希。build.ps1 靠它判断要不要重新解压
$dll = Join-Path $PackageDir 'win-x64\libmpv-2.dll'
if (-not (Test-Path $dll)) { throw "解压后仍找不到 $dll。" }

$manifest = [ordered]@{
    archive = $asset.name
    variant = $Variant
    tag     = $release.tag_name
    dll     = 'libmpv-2.dll'
    size    = (Get-Item $dll).Length
    sha256  = (Get-FileHash -Path $dll -Algorithm SHA256).Hash
}

$manifestPath = Join-Path $PackageDir 'manifest.json'
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath
Write-Host "已写入清单 $manifestPath"
Write-Host "  7z：$($asset.name)"
Write-Host "  dll：$($manifest.sha256)"
