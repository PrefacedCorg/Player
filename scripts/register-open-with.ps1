# 把播放器注册进 Windows 的"打开方式"列表（全部写在 HKCU，无需管理员权限）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File scripts/register-open-with.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/register-open-with.ps1 -ExePath "D:\Player\Player.App.exe"
#   powershell -ExecutionPolicy Bypass -File scripts/register-open-with.ps1 -Unregister
#
# 注册后：
#   1) 右键媒体文件 → 打开方式 → 会出现"Player 大屏播放器"
#   2) 把文件拖到 exe 图标上可直接启动播放（这条不依赖注册，本来就支持）

[CmdletBinding()]
param(
    [string]$ExePath,
    [switch]$Unregister,
    [switch]$IncludeImages
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $ExePath = Join-Path $PSScriptRoot '..\src\Player.App\bin\Debug\net10.0\Player.App.exe'
}

# 扩展名列表需与 src/Player.Playback/MediaKindResolver.cs 保持一致
$videoExtensions = @(
    '.mp4', '.mkv', '.mov', '.avi', '.wmv', '.flv', '.webm', '.m4v',
    '.mpg', '.mpeg', '.ts', '.m2ts', '.rmvb', '.rm', '.3gp', '.vob',
    '.f4v', '.asf', '.divx',
    '.mts', '.m2t', '.tp', '.trp', '.mxf', '.dv', '.amv', '.ogv', '.m2v'
)
$audioExtensions = @(
    '.mp3', '.flac', '.wav', '.aac', '.m4a', '.ogg', '.oga', '.opus',
    '.wma', '.ape', '.alac', '.aif', '.aiff', '.mka', '.ac3', '.dts', '.amr'
)
$imageExtensions = @('.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp', '.tif', '.tiff', '.avif')

$extensions = $videoExtensions + $audioExtensions
if ($IncludeImages) { $extensions += $imageExtensions }

if (-not (Test-Path $ExePath)) {
    throw "找不到 exe：$ExePath`n请先执行 dotnet build，或用 -ExePath 指定发布后的可执行文件路径。"
}

$exe = (Resolve-Path $ExePath).Path
$exeName = Split-Path $exe -Leaf
$friendlyName = 'Player 大屏播放器'
$appKeyPath = "Software\Classes\Applications\$exeName"

if ($Unregister) {
    Remove-Item -Path "HKCU:\$appKeyPath" -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($ext in $extensions) {
        $progId = 'PlayerMedia.' + $ext.TrimStart('.')
        Remove-Item -Path "HKCU:\Software\Classes\$progId" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path "HKCU:\Software\Classes\$ext\OpenWithProgids" -Name $progId -ErrorAction SilentlyContinue
    }

    Write-Host "已注销：$exeName 的打开方式注册。" -ForegroundColor Yellow
    return
}

# ---- 1) 注册到"打开方式"候选列表 ----
New-Item -Path "HKCU:\$appKeyPath\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\$appKeyPath\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
Set-ItemProperty -Path "HKCU:\$appKeyPath" -Name 'FriendlyAppName' -Value $friendlyName
New-Item -Path "HKCU:\$appKeyPath\SupportedTypes" -Force | Out-Null

foreach ($ext in $extensions) {
    New-ItemProperty -Path "HKCU:\$appKeyPath\SupportedTypes" -Name $ext -Value '' -PropertyType String -Force | Out-Null
}

# ---- 2) 每种扩展名注册 ProgID，使其出现在右键"打开方式"子菜单 ----
$openWithKeys = @{}
foreach ($ext in $extensions) {
    $progId = 'PlayerMedia.' + $ext.TrimStart('.')
    $progKeyPath = "HKCU:\Software\Classes\$progId"

    New-Item -Path "$progKeyPath\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path $progKeyPath -Name '(default)' -Value "$friendlyName ($ext)"
    Set-ItemProperty -Path "$progKeyPath" -Name 'FriendlyTypeName' -Value "$friendlyName ($ext)"
    Set-ItemProperty -Path "$progKeyPath\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""

    $openWith = "Software\Classes\$ext\OpenWithProgids"
    if (-not $openWithKeys.ContainsKey($openWith)) {
        $openWithKeys[$openWith] = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($openWith)
    }

    # OpenWithProgids 的值应为 REG_NONE 空值
    $openWithKeys[$openWith].SetValue($progId, [byte[]]@(), [Microsoft.Win32.RegistryValueKind]::None)
}

foreach ($key in $openWithKeys.Values) { $key.Dispose() }

$imageNote = ''
if ($IncludeImages) { $imageNote = ' + 图片' }

Write-Host "注册完成：" -ForegroundColor Green
Write-Host "  程序        $exe"
Write-Host "  显示名称    $friendlyName"
Write-Host "  支持扩展    $($extensions.Count) 种（视频 + 音频$imageNote）"
Write-Host ""
Write-Host "使用方式：" -ForegroundColor Cyan
Write-Host "  1) 右键媒体文件 → 打开方式 → $friendlyName"
Write-Host "  2) 直接把文件拖到 Player.App.exe 上（无需注册）"
Write-Host "  3) 设为默认：右键 → 打开方式 → 选择其他应用 → 选中后勾选'始终使用'"
Write-Host ""
Write-Host "注意：exe 路径变了（例如重新发布到别的目录）需要重新执行本脚本。" -ForegroundColor Yellow