# 本地构建入口：确保 libmpv 就绪后编译
#
# 两份清单：
#   third_party/mpv/manifest.json                  入库，由 update-libmpv 工作流 /
#                                                  fetch-libmpv.ps1 维护，记录当前
#                                                  7z 文件名与它应解出的 dll 哈希
#   third_party/mpv/win-x64/manifest.local.json    纯本地（目录被 .gitignore 忽略），
#                                                  记录「哪个 7z 已在本机解压校验完成」
#                                                  与 yes/no 状态
#
# 判定（对照两份清单）：
#   本地清单不存在（首次运行）        → 解压 → 核对 dll 哈希 → 创建本地清单（yes）
#   本地状态是 no（上次中途失败）      → 同上，重走一遍
#   本地 archive ≠ 仓库 archive       → 先落盘「新文件名 + no」→ 解压 → 核对 → 改回 yes
#   archive 一致但 dll 被清理过        → 解压 → 核对 → yes
#   archive 一致、状态 yes、dll 在     → 跳过解压，直接编译
#
# 状态为什么先写 no：解压或校验中途失败时，本地清单停在 no，
# 下次运行看到 no 就会重走完整流程，不会拿着旧 dll 编译。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Configuration Debug
#   powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -SkipRestore

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    # 跳过还原（已还原过时可省几秒）
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mpvDir = Join-Path $root 'third_party\mpv'
$repoManifestPath = Join-Path $mpvDir 'manifest.json'
$localManifestPath = Join-Path $mpvDir 'win-x64\manifest.local.json'
$dll = Join-Path $mpvDir 'win-x64\libmpv-2.dll'

# 仓库清单是唯一的比对基准
if (-not (Test-Path $repoManifestPath)) {
    throw "缺少 $repoManifestPath（入库文件）。请拉取仓库，或执行 scripts/fetch-libmpv.ps1 生成。"
}
$repo = Get-Content $repoManifestPath -Raw | ConvertFrom-Json

$archivePath = Join-Path $mpvDir $repo.archive
if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "manifest.json 记录的 $($repo.archive) 不在仓库里，状态不完整。请检查 third_party\mpv\。"
}

# 本地清单可能还没有（首次运行）
$local = $null
if (Test-Path $localManifestPath) {
    $local = Get-Content $localManifestPath -Raw | ConvertFrom-Json
}

function Set-LocalManifest {
    param([ValidateSet('yes', 'no')][string]$Status)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $localManifestPath) | Out-Null
    ([ordered]@{ archive = $repo.archive; status = $Status } | ConvertTo-Json) |
        Set-Content -Path $localManifestPath
}

$needExtract = $false
if (-not $local) {
    Write-Host '本地清单不存在（首次运行）。'
    $needExtract = $true
}
elseif ($local.status -ne 'yes') {
    Write-Host '本地状态为 no（上次解压/校验未完成）。'
    $needExtract = $true
}
elseif ($local.archive -ne $repo.archive) {
    Write-Host "7z 已更新：本地 $($local.archive) → 仓库 $($repo.archive)。"
    $needExtract = $true
}
elseif (-not (Test-Path $dll)) {
    Write-Host 'dll 不存在（被清理过）。'
    $needExtract = $true
}

if ($needExtract) {
    # 先落盘「当前 7z + no」：此后任何一步失败，下次都会重走完整流程
    Set-LocalManifest -Status 'no'

    Write-Host "解压 $($repo.archive) ..."
    & (Join-Path $PSScriptRoot 'extract-libmpv.ps1') -Archive $repo.archive -Force

    $actual = (Get-FileHash -Path $dll -Algorithm SHA256).Hash
    if ($actual -ne $repo.sha256) {
        throw @"
解压出的 dll 哈希与 manifest.json 记录不一致。
  记录：$($repo.sha256)
  实际：$actual
7z 与 manifest.json 可能不配套，请执行 scripts/fetch-libmpv.ps1 -Force 重新生成。
"@
    }

    Set-LocalManifest -Status 'yes'
    Write-Host 'dll 校验通过，本地清单已标记就绪。'
}
else {
    Write-Host "libmpv 已就绪（$($repo.archive)），跳过解压。"
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
