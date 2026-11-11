# Hitokoto Taskbar

当前源码版本：**1.2.0**。可同时在两台显示器的任务栏显示同一句一言，并通过 GitHub 检查更新。兼容旧版配置。

在 Windows 11 任务栏左侧空白处显示一言的独立 WPF 小工具。目标平台是 **Windows 11 x64、.NET 10**。不修改或注入 Explorer，不安装服务或驱动，不需要管理员权限。

每个需要显示一言的任务栏使用一个紧贴文字大小的透明窗口。任务栏和控件的位置通过 Win32 与 Windows UI Automation 读取；只在“开始”按钮左侧找到可确认的空白区域时显示，并给可交互控件留出约 8 DIP 间距。遇到无法识别的任务栏布局时会隐藏文字，并保留托盘入口。这是为了避免文字覆盖开始按钮、搜索、应用图标、小组件、托盘和时钟。

## 直接运行

发布后的文件位于：

```text
artifacts/publish/win-x64/HitokotoTaskbar.exe
```

双击 `HitokotoTaskbar.exe` 即可。也可以解压 `artifacts/HitokotoTaskbar-win-x64.zip` 后运行其中的 EXE。建议把 EXE 放在固定目录，再启用开机启动。自包含发布不要求用户另外安装 .NET Runtime。

启动后先显示上次缓存的一言，同时异步请求新语句。默认每 5 分钟请求一次，网络失败保留上一条有效语句。

这个程序不会把自己注册为服务。退出菜单会取消网络请求和计时任务，移除托盘图标并释放原生事件钩子。重复启动通过当前用户会话内的单实例机制处理。

## 操作

| 操作 | 作用 |
| --- | --- |
| 单击任务栏文字 | 尝试获取下一句 |
| 悬停文字 | 显示完整语句、作者和出处 |
| 右键文字 | 下一句、复制、官网、设置、检查更新、开机启动和退出 |
| 托盘图标 | 管理程序；任务栏文字暂时隐藏时仍可操作 |
| 设置中的“显示托盘图标” | 控制是否保留托盘入口 |

手动刷新有至少 15 秒的请求间隔；连续单击不会不断向 API 发请求。刷新过程中不会并发启动相同请求。

覆盖窗口不接收键盘焦点，不进入 Alt+Tab，也不会出现在任务栏应用列表中。透明窗口不会铺满整个任务栏。没有对文字窗口使用会让鼠标全部穿透的 `WS_EX_TRANSPARENT`：**只有绘制出来的字形像素能接收鼠标；字间空隙和其他完全透明像素会穿透到下面的任务栏。** 点击时请点文字本身。

设置窗口打开时不主动抢焦点；**点击设置中的输入框后，它需要接收键盘焦点才能编辑。** 打开官网会将操作交给默认浏览器，浏览器是否切到前台由 Windows 和浏览器决定，程序无法承诺外部浏览器也永远不改变焦点。

程序还提供命令行入口。关闭托盘图标且文字暂时隐藏时，也能打开设置或退出：

```powershell
.\HitokotoTaskbar.exe --settings
.\HitokotoTaskbar.exe --refresh
.\HitokotoTaskbar.exe --exit
```

上述命令会通知已有实例。只读布局诊断和隔离运行检查不改变真实用户配置：

```powershell
.\HitokotoTaskbar.exe --diagnose "$PWD\layout-diagnostic.json"
.\HitokotoTaskbar.exe --smoke-test "$PWD\smoke-test.json"
# 观察默认 5 分钟定时刷新，可运行 330 秒。
.\HitokotoTaskbar.exe --smoke-test "$PWD\five-minute\report.json" --smoke-seconds 330
```

诊断模式写入原生显示器与任务栏探测结果后退出。冒烟模式默认运行 18 秒，可用 `--smoke-seconds` 指定 18–600 秒；配置写入报告所在目录的 `smoke-state` 子目录，不读取真实用户的配置，也不修改开机启动项。重复使用同一报告目录时，会复用该隔离配置。

冒烟报告包含成功获取次数、各显示器的实际窗口位置、窗口样式、DPI、资源占用、GitHub 检查结果及开始/结束时的前台窗口句柄。目标任务栏可见时，还会保存对应的 PNG，只截取任务栏区域。冒烟测试不会打开浏览器。

## 修改文字样式

右键文字或托盘图标，打开“设置”。可以调整：

- 字体、字号、文字颜色和整体透明度。
- 水平偏移、垂直偏移、最大宽度及显示器。
- 引号图标和过渡动画。
- 中文分类多选、最短和最长字数、刷新间隔和托盘图标。

默认样式：`Microsoft YaHei UI`、13 DIP、普通字重、`#F5EAF8`、不透明度 1、完全透明背景、没有边框。默认距任务栏左边缘约 24 DIP，最大宽度 500 DIP；如果小组件或其他任务栏控件占用了左侧空间，实际位置与宽度会进一步收缩或隐藏。

文字长度超过可用宽度时显示省略号。完整内容仍可通过悬停或“复制当前语句”获取。位置和尺寸设置使用 DIP，最终定位使用当前显示器的物理像素；不依赖固定的 4K 分辨率坐标。

## 开机自动启动

默认关闭。在右键菜单中勾选“开机自动启动”即可启用，再次单击即可关闭。程序只使用当前用户的启动项，不修改系统级启动项，也不要求管理员权限。

启用后不要移动或重命名 EXE；若移动，先关闭启动项，再从新位置启动并重新启用。正式使用应对发布后的 EXE 设置启动，而不是对构建目录中的调试程序设置。

## 检查更新

默认在启动后检查一次，持续运行时每 24 小时再检查。也可以右键一言或托盘图标，点“检查更新”。程序只比较本机版本与 GitHub 最新正式 Release，不需要登录。发现更高版本时打开对应的 GitHub 下载页，由你选择下载和替换文件。

自动检查只为同一个新版本打开一次网页；锁屏或全屏时会等到恢复后再打开。设置中的“自动检查更新”可以关闭，手动检查仍可用。

## 配置、缓存和日志

配置保存在：

```text
%LOCALAPPDATA%\HitokotoTaskbar\settings.json
```

其中保存样式、显示器选择、API、分类、句子字数范围、刷新间隔、自动启动状态以及最后一条成功语句。可以在程序退出后手动编辑，随后重新运行。优先使用设置界面，以免输入格式错误。升级时会沿用旧分类，并读取旧 API 地址里的 `min_length` 和 `max_length`。

配置损坏时程序保留 `settings.json.corrupt` 备份和错误日志，然后使用默认配置。日志位于 `%LOCALAPPDATA%\HitokotoTaskbar\logs\app.log`，单文件上限 512 KiB，另外保留两份滚动备份，总量约 1.5 MiB。日志不记录 API 返回的完整内容；自定义 API 地址不应包含密码或其他秘密。

## 显示器、任务栏和全屏

- 默认跟随主显示器；可指定单屏，或在“设置 → 显示位置 → 显示器”选“所有显示器（同时显示）”。
- 所有屏幕共用一句话和刷新计时。Windows 需要为副屏显示任务栏，且该任务栏要有可安全使用的空白。
- 单屏模式下，所选显示器断开时回退到主屏；所有显示器模式会随连接状态增加或移除对应的显示窗口。
- 显示器、分辨率、缩放比例、主屏、任务栏布局变化后重新测量。
- 使用 Per-Monitor DPI Awareness V2；Win32 坐标保持为物理像素，WPF 字体和布局尺寸按所在显示器的 DPI 换算。
- Explorer 重启、睡眠唤醒后会重新发现任务栏并恢复。
- **只要启用了任务栏自动隐藏，程序就持续隐藏一言，即使鼠标临时唤出任务栏。** 这是当前版本的保守策略，用来避免任务栏收起动画期间文字孤立悬浮；关闭自动隐藏后恢复。托盘入口不受此策略影响。
- 全屏只隐藏那块屏的文字，其余屏幕继续显示，退出全屏后恢复。
- 全屏判断比较前台窗口的实际边界与目标显示器完整边界。普通最大化窗口通常只占工作区，不能仅凭“最大化”状态判成全屏。
- 状态检查结合系统事件与低频检查；没有帧级忙轮询或常驻外部命令。

当前版本面向底部、居中图标的任务栏。**开始按钮位于最左侧、非底部任务栏或无法识别控件时，不会强行显示。** Windows 11 的任务栏内部 UI Automation 结构并不是稳定的公共布局协议。系统更新、第三方任务栏改造、特殊桌面/远程会话可能使空白区域探测失败；程序会保守隐藏，不能保证适配所有未来任务栏版本。

控件探测放在独立的 MTA 工作线程上，单次等候上限 1.5 秒，布局缓存 5 秒并允许事件使其失效。这样任务栏的 UI Automation 提供程序变慢时，不会阻塞 WPF 主线程。真实游戏、不同显示缩放、热插拔等结果以 [验证记录](docs/VALIDATION.md) 为准。

## 一言 API

默认请求：

```text
https://v1.hitokoto.cn/?encode=json&min_length=0&max_length=28
```

在“设置 → 内容与刷新”里直接勾选中文分类，例如“文学”和“诗词”。全部不勾选时不限分类。最短字数默认 0（不设下限），最长默认 28；输入整数，最短不能大于最长。保存后，下次刷新按新范围抽取。使用官方接口时无需修改 API 地址。

程序将所选分类和字数转换为 API 参数，并解析 `hitokoto`、`from`、`from_who`、`uuid`；任务栏只显示正文。类别及接口行为请以[一言官方文档](https://developer.hitokoto.cn/sentence/)为准。

网络请求使用 `HttpClient` 异步执行，设有超时、取消、响应大小限制和节流。默认 5 分钟，配置的最小刷新间隔是 1 分钟。HTTP 错误、超时、错误 JSON、空语句和断网不会清空已缓存的一言。

## 从源码构建

需要 Windows 11 x64 和稳定版 **.NET 10 SDK**；只安装 Runtime 无法构建。无需 Visual Studio，也不需要管理员权限。

项目优先使用 `.tools\dotnet\dotnet.exe`，找不到时使用系统 PATH 中的 `dotnet`。`global.json` 允许使用已安装的稳定版 .NET 10 新功能带 SDK。

在项目根目录执行：

```powershell
# 还原、Release 编译、运行自动化测试
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 再次构建及测试，然后发布独立单文件 EXE 与 ZIP
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
```

已有对应 SDK 时，也可以直接运行：

```powershell
dotnet restore .\HitokotoTaskbar.slnx
dotnet build .\HitokotoTaskbar.slnx -c Release --no-restore
dotnet test .\HitokotoTaskbar.slnx -c Release --no-build
dotnet publish .\src\HitokotoTaskbar\HitokotoTaskbar.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
  -p:DebugType=embedded -o .\artifacts\publish\win-x64
```

未安装 SDK 时，可以从[微软 .NET 下载页](https://dotnet.microsoft.com/download/dotnet/10.0)安装，也可以使用微软的 `dotnet-install.ps1` 安装到项目的 `.tools\dotnet` 目录。构建脚本本身不自动安装全局软件，也不更改系统 PATH。

发布目标为 `win-x64`，显式关闭裁剪。自包含 WPF 会包含 .NET 框架，因此 EXE 体积明显大于源代码；单文件启动时仍可能在 `%TEMP%\.net` 解出原生组件。这是 .NET 单文件发布机制，并不意味着用户要安装 Runtime。[微软单文件发布说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

`artifacts/SHA256SUMS.txt` 保存 EXE 和 ZIP 的 SHA-256。测试报告位于 `artifacts/test-results/`，按项目分别保存为 `HitokotoTaskbar.Tests.trx` 和 `HitokotoTaskbar.WindowsTests.trx`。只有日志和验证记录明确报告成功的步骤，才算本次实际验证。

## 工程结构

```text
src/
  HitokotoTaskbar/          WPF 程序、设置、托盘和 Win32/UI Automation
    Native/                任务栏、显示器、全屏、原生窗口样式与事件
  HitokotoTaskbar.Core/     配置、一言 API、日志和可独立测试的几何规则
tests/
  HitokotoTaskbar.Tests/    API、配置、几何与控件避让规则测试
  HitokotoTaskbar.WindowsTests/ WPF/Win32 实际窗口与任务栏集成测试
scripts/common.ps1         SDK 查找和命令错误处理
scripts/package-release.ps1 更新便携包中的文档并重新计算校验和，不重新编译
build.ps1                  还原、编译、测试
publish.ps1                自包含发布、便携 ZIP、校验和
docs/VALIDATION.md          本次实际结果与人工验收步骤
```

生产程序只使用 Windows/.NET 自带功能；测试项目使用测试框架包。单文件不使用 WPF 裁剪、Electron、Chromium、WebView2 或浏览器容器。

## 验证范围

已在本机 Windows 11（构建 26300）和 .NET SDK 10.0.404 上完成 Release 编译，0 警告、0 错误；最终构建的核心自动化测试 52/52、Windows 集成测试 10/10 全部通过。Windows 测试实际创建 WPF 窗口，检查非激活样式、物理坐标、文字省略和六个菜单动作的事件路由，但没有模拟真实鼠标点击。

真实一言 API 已成功返回中文；副屏任务栏截图中中文清晰，文字处于图标左侧空白内。隔离运行开始与结束时的前台窗口未改变。普通启动的重复实例和 `--exit` 已实际验证：只保留一个正常实例，退出后没有遗留正常实例。将 API 改为故意不可用的地址后，程序保留预置缓存并正常退出；这验证了请求失败路径，没有实际断开机器网络。

已通过最终 EXE 的设置界面实际启用、保存、重新打开并关闭开机启动，并核验当前用户 `HKCU` 启动项正确写入和移除。测试结束时自动启动关闭、程序进程数为 0。没有执行注销/重新登录，因此登录后的自动启动仍需人工验证。

自包含 EXE 已实际运行 315 秒，获得两次成功响应，确认默认约 5 分钟刷新。该次进程累计 CPU 时间约 2.80 秒，结束时工作集约 161.5 MiB；这是包含启动的短时实测，不是长时间内存稳定性结论。

最终发布后的 EXE 又完成了 18 秒隔离冒烟检查：API 成功、主屏覆盖窗口可见、144 DPI 下位置正确、前台窗口未变，并完成退出清理。当前单文件约 165.2 MiB，包含 .NET 10/WPF 运行时。

原生探测已读取两台 3840×2160 显示器及其任务栏，当前两屏都是 150% 缩放（144 DPI）。设置窗口已实际打开，通过界面选择副屏并成功保存至隔离配置；副屏覆盖窗口的实测物理位置与该设置一致。这些结果**不等于**混合 DPI、热插拔、游戏、Explorer 重启或开机登录已经验证。

自动化测试用于核实 API 解析和异常、请求节流、配置恢复、日志上限、DPI/坐标计算、控件避让、全屏几何判断等可重复行为。它们不能代替真实 Explorer、显卡驱动、游戏、任务栏样式和显示器热插拔的验证。

所有实际执行结果以及尚未执行的人工步骤，都在 [docs/VALIDATION.md](docs/VALIDATION.md)。未经操作确认的功能不会写成“已通过”。
