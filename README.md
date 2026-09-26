# Player

基于 Avalonia 12 + libmpv 的大屏媒体播放器，目标框架 `net10.0`，Windows x64。

## 编译

### 前置条件

- **.NET 10 SDK**（`dotnet --version` 应显示 10.0.x）
- Windows x64
- PowerShell（仅用于下载 libmpv）

### 第 1 步：准备 libmpv 并编译

libmpv 不随 NuGet 分发，官方也没有单独的预编译 dll。仓库里存的是它的 7z 包
（约 32 MB，来自 [shinchiro/mpv-winbuild-cmake](https://github.com/shinchiro/mpv-winbuild-cmake)
的 `mpv-dev-x86_64-v3`），编译前需要解压出 dll。**第一次直接跑这个就行**：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
```

它会自动完成：解压 7z → 核对 dll 哈希 → 编译。之后再跑会跳过解压、直接编译。

> 需要 7z：Windows 装 7-Zip，Linux 装 `p7zip-full`。
> `x86_64-v3` 需要 CPU 支持 AVX2；老机器改用 `x86_64` 变体。

想拆成两步手动做也可以：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/extract-libmpv.ps1
dotnet build
```

产出的 dll 在 `third_party\mpv\win-x64\libmpv-2.dll`（约 118 MB）。该目录写在 `.gitignore` 里，
仓库只存 7z 不存 dll，所以换机器或清理后要重新解压一次。

`libmpv-2.dll` 会在编译时自动复制到输出目录（`Player.App.csproj` 里的 `CopyToOutputDirectory`），不用手动拷。
默认 Debug 配置，产物在 `src\Player.App\bin\Debug\net10.0\`；要 Release 就 `.\scripts\build.ps1 -Configuration Release`。

**判断要不要重新解压的依据**：仓库的 `third_party\mpv\manifest.json`（7z 文件名 + dll 哈希）
与本地的 `win-x64\manifest.local.json`（已解压完成的包名 + yes/no 状态）。两者不一致、
或状态是 no（上次中途失败）、或 dll 不在，都会重新解压并核对哈希。

要更新 7z 包（手动触发 GitHub Actions 的 update-libmpv，或本地执行）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fetch-libmpv.ps1 -Force
```

### 运行

```bash
dotnet run --project src/Player.App
```

或直接执行 `src\Player.App\bin\Debug\net10.0\Player.App.exe`。把媒体文件拖到 exe 图标上可直接播放。

跑测试：

```bash
dotnet test
```

## 可选

**发布到目录**

```bash
dotnet publish src/Player.App/Player.App.csproj -c Release -r win-x64 -o publish\win-x64
```

指定 `-r` 后默认是自包含发布（体积较大，但目标机器不用装 .NET）；
想依赖本机运行时就再加 `--self-contained false`。

**注册到 Windows「打开方式」列表**（全部写 HKCU，无需管理员）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/register-open-with.ps1
```

取消注册加 `-Unregister`，指定其他路径用 `-ExePath "D:\Player\Player.App.exe"`。

## 常见问题

**运行报错说找不到 libmpv**

编译时不会因为缺 libmpv 而报错（csproj 里是 `Condition="Exists(...)"`），但运行时会失败。先跑
`scripts/extract-libmpv.ps1`，再检查 `third_party\mpv\win-x64\libmpv-2.dll` 是否生成、是否大于 10 MB。

**`dotnet restore` / `dotnet build` 卡很久不动**

还原阶段会访问 NuGet 远程源，网络不通时会一直等待。若所有包都已在本地缓存
（`%USERPROFILE%\.nuget\packages`），可以用离线配置跳过联网：

```bash
dotnet restore --configfile <只包含 packageSources/clear 的 nuget.config>
dotnet build --no-restore
```

**Avalonia 相关类型找不到**

本项目用 Avalonia 12，它与 11.x 有若干破坏性变更（例如 `ShutdownMode` 从
`Avalonia.Controls.ApplicationLifetimes` 移到了 `Avalonia.Controls`，`TextBox.Watermark` 改名为
`PlaceholderText`）。改动涉及的类型可按同样思路在
`%USERPROFILE%\.nuget\packages\avalonia\<版本>\lib\net10.0\*.xml` 里搜确认新的完全限定名。

## 项目结构

| 项目 | 说明 |
| --- | --- |
| `src/Player.App` | Avalonia 界面层：主窗口、设置窗口、控制栏控件 |
| `src/Player.Playback` | 播放内核：`IMediaEngine` 与基于 libmpv 的实现 |
| `src/Player.Library` | 媒体库：目录扫描与排序 |
| `src/Player.Platform` | 平台相关：启动耗时跟踪等 |
| `tests/Player.Tests` | xunit 单元测试 |

控制栏是可编排的组件树：分组 / 滚动 / 滑动 / 堆叠四种容器可嵌套子控件，设置页支持从组件库拖放排序，
控件可见性由规则系统（`src/Player.App/Rules/`）在运行时求值。
