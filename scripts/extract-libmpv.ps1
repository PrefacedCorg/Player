# 从入库的 7z 包里解压出 libmpv-2.dll
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/extract-libmpv.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/extract-libmpv.ps1 -Force
#
# 依赖 7z：Windows 装 7-Zip（GitHub windows-latest 已预装），Linux 装 p7zip-full。
# 产出：third_party/mpv/win-x64/libmpv-2.dll（该目录已被 .gitignore 忽略）

[CmdletBinding()]
param(
    # 指定 7z 完整文件名（优先于变体匹配；build.ps1 用它保证解的是 manifest.json 记录的那个包）
    [string]$Archive,

    [string]$Variant = 'x86_64-v3',

    [string]$PackageDir = (Join-Path $PSScriptRoot '..\third_party\mpv'),

    [string]$TargetDir = (Join-Path $PSScriptRoot '..\third_party\mpv\win-x64'),

    # 即使目标 dll 看起来是最新的也重新解压
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$minSize = 10MB
$target = Join-Path $TargetDir 'libmpv-2.dll'

# 注意：局部变量叫 $archiveFile，不能叫 $archive——与参数 $Archive 大小写不敏感同名，
# [string] 约束会把赋进去的 FileInfo 静默 ToString() 成字符串，后续 .FullName 就取不到了。
if ($Archive) {
    $path = Join-Path $PackageDir $Archive
    if (-not (Test-Path -LiteralPath $path)) {
        throw "在 $PackageDir 下找不到指定的 $Archive。"
    }
    $archiveFile = Get-Item -LiteralPath $path
}
else {
    # 变体可能留下多个版本的包，文件名里带日期，取字典序最后一个（即最新）
    $archiveFile = Get-ChildItem -Path $PackageDir -Filter "mpv-dev-$Variant-*.7z" -File -ErrorAction SilentlyContinue |
        Sort-Object Name | Select-Object -Last 1

    if (-not $archiveFile) {
        throw "在 $PackageDir 下找不到 mpv-dev-$Variant-*.7z。请先执行 scripts/fetch-libmpv.ps1 下载。"
    }
}

New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

if ((Test-Path $target) -and -not $Force) {
    $existing = Get-Item $target
    if ($existing.Length -gt $minSize -and $existing.LastWriteTimeUtc -ge $archiveFile.LastWriteTimeUtc) {
        Write-Host "libmpv-2.dll 已是最新（来自 $($archiveFile.Name)），跳过解压。要重解请加 -Force。"
        return
    }
}

function Find-SevenZip {
    foreach ($name in @('7z', '7zz')) {
        $cmd = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($cmd) { return $cmd.Source }
    }

    if ($env:ProgramFiles) {
        $candidate = Join-Path $env:ProgramFiles '7-Zip\7z.exe'
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

$sevenZip = Find-SevenZip
if (-not $sevenZip) {
    throw '找不到 7z：Windows 请安装 7-Zip，Linux 请执行 apt-get install -y p7zip-full，或将其加入 PATH。'
}

Write-Host "解压 $($archiveFile.Name) -> $target ..."
$output = & $sevenZip e $archiveFile.FullName "-o$TargetDir" 'libmpv-2.dll' -y 2>&1 | Out-String
$code = $LASTEXITCODE
if ($code -ne 0) {
    throw "7z 解压失败（退出码 $code）。`n$output"
}
Write-Host $output.TrimEnd()

if (-not (Test-Path $target)) { throw '7z 里没有 libmpv-2.dll，请检查包内容。' }

$size = (Get-Item $target).Length
if ($size -le $minSize) { throw "解压出的 dll 过小（$size 字节），疑似异常。" }

Write-Host "完成：$target（$([math]::Round($size / 1MB, 1)) MB）"

