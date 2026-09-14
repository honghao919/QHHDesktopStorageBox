<p align="center">
  <img src="https://raw.githubusercontent.com/honghao919/QHHDesktopStorageBox/main/src/QHHDesktopStorageBox.App/Assets/app.png" alt="QHH Desktop Storage Box Logo" width="128" height="128" />
</p>

<h1 align="center">QHH Desktop Storage Box</h1>

<p align="center">
  <a href="https://github.com/honghao919/QHHDesktopStorageBox/actions/workflows/ci.yml"><img src="https://github.com/honghao919/QHHDesktopStorageBox/actions/workflows/ci.yml/badge.svg" alt="Windows CI" /></a>
  <a href="https://github.com/honghao919/QHHDesktopStorageBox/releases/latest"><img src="https://img.shields.io/github/v/release/honghao919/QHHDesktopStorageBox?display_name=tag" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/version-1.3.14-blue" alt="Version" />
  <img src="https://img.shields.io/badge/license-PolyForm%20Noncommercial%201.0.0-orange" alt="License" />
  <img src="https://img.shields.io/badge/docs%20%26%20assets-CC%20BY--NC--SA%204.0-lightgrey" alt="Docs & Assets License" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/platform-Windows-blue" alt="Platform" />
</p>

QHH Desktop Storage Box 是一款基于原生 WPF 构建的轻量级 Windows 桌面文件收纳工具。专为桌面美化和日常文件收纳设计：将常用文件拖入桌面小收纳盒，快速打开，让临时工作资料井然有序。

English: QHH Desktop Storage Box is a lightweight Windows desktop file drawer built with native WPF. It is designed for desktop beautification and daily file staging.

## 功能特性

- **普通收纳盒** — 将拖入的文件或文件夹移入 QHH Desktop Storage Box 的应用数据存储目录
- **映射收纳盒** — 仅存储绝对路径引用，源文件保留在原位
- **临时收件箱** — 先把文件集中收进来，稍后再移动到合适的收纳盒
- **智能收纳盒** — 按目录、扩展名、名称和修改时间规则自动维护路径引用，不移动源文件
- **待办收纳盒** — 支持添加、完成和归档；双击内容、按 F2 或点击编辑按钮修改事项，Enter 保存、Esc 取消；删除单项后可在 10 秒内撤销
- **像素收纳盒** — 像素风格的收纳盒，为桌面增添趣味
- **桌面浮动窗口** — 每个收纳盒显示为精美的浮动桌面窗口，支持自由拖放定位
- **普通盒网格/列表视图** — 普通盒和像素盒可分别切换网格图标或紧凑列表视图
- **系统应用收纳** — 可从开始菜单应用选择器加入，也可尝试从开始菜单直接拖入；UWP 应用保存 Windows 应用标识，传统程序保存绝对路径引用
- **窗口位置记忆** — 自动记住每个收纳盒在桌面上的位置
- **窗口卷起** — 可手动将普通和映射收纳盒收起到标题栏，并记住每个盒子的卷起状态
- **系统图标** — 拖入的文件显示系统原生图标
- **文件名显示** — 网格视图可按收纳盒显示或隐藏文件名
- **拖出支持** — 可以将项目从收纳盒中拖出作为文件放置
- **跨盒拖放** — 支持在收纳盒之间拖放移动图标
- **多选批量操作** — 在控制台使用 Ctrl/Shift 多选后批量打开、导出、移动或删除
- **导入预检** — 移动前统计文件数量、大小、跨盘和重名情况，可选择跳过重名或保留两份
- **操作撤销** — 持久化记录导入、移动、导出和删除，支持撤销最近一次文件操作
- **快捷面板** — 按 `Ctrl+Alt+W` 跨所有收纳盒搜索并打开项目
- **拼音搜索** — 快捷面板支持中文全拼和拼音首字母匹配
- **三套主题** — 清透雅致 / 玻璃光泽 / 水晶棱镜
- **图标大小** — 超大 / 大 / 中 / 小 四档可调
- **可调透明度** — 可分别调节桌面盒子、盒子边线和图标背景框透明度
- **开机自启动** — 可在设置中开启/关闭
- **检查更新** — 自动检测 GitHub Releases 新版本
- **原位还原删除** — 删除收纳项或收纳盒时，普通/像素盒文件恢复到原来的位置；原位置不可用则回退到桌面，重名自动加后缀；映射盒只删除引用
- **窗口恢复** — 可从主页收纳盒菜单恢复单个窗口，或从系统托盘显示全部收纳盒
- **桌面图标隐藏** — 可在设置中隐藏 Windows 桌面文件、文件夹和快捷方式；也可启用双击桌面空白区域快速切换，不移动或删除文件
- **自动隐藏** — 开启后鼠标移开时，按设置的透明度隐藏收纳盒内的图标与文字，并可同步隐藏收纳盒标题与边框；悬停时可选择仅显示被悬停的收纳盒，或让全部收纳盒一起显示
- **图标名称模式** — 悬停提示可在完整文件路径与精简文件名之间切换，快捷方式（`.lnk`）自动去掉扩展名
- **系统托盘** — 最小化到系统托盘，不占用任务栏
- **单实例运行** — 防止重复启动
- **完整备份与恢复** — 将数据库和普通盒文件导出为 ZIP，并安全恢复到新的空目录
- **数据升级保护** — 数据库结构升级前自动保留最近 5 个版本化数据库备份
- **失效引用检查** — 扫描映射盒和智能盒中已经失效的源路径
- **诊断报告** — 汇总版本、数据目录、容量、失效引用和最近日志，并自动隐藏用户主目录

## 下载与安装

请从 [GitHub Releases](https://github.com/honghao919/QHHDesktopStorageBox/releases/latest) 下载最新版本。

- 安装版：运行 `QHHDesktopStorageBox-Setup-vX.Y.Z-x64.exe`。
- 便携版：完整解压 `QHHDesktopStorageBox-vX.Y.Z-win-x64.zip` 后运行 `QHHDesktopStorageBox.App.exe`。
- 不要只复制便携版中的单个 EXE；WPF 运行所需的附属文件必须与 EXE 一起保留。
- 不要以管理员身份运行。管理员进程无法接收普通桌面拖放。

正式发布同时提供安装包、便携 ZIP 和各自的 SHA-256 文件。更新程序会拒绝没有已发布校验值的更新包。

## 数据备份与恢复

设置页“数据安全与恢复”提供四项操作：

- **创建完整备份**：导出 SQLite 数据库、普通盒文件、抽屉盒/像素盒/收件箱存储和备份清单。
- **从备份恢复**：先验证备份清单、数据库和压缩包路径，再恢复到用户选择的空文件夹。
- **检查失效引用**：扫描映射盒和智能盒的源路径，不会移动或删除任何文件。
- **生成诊断报告**：输出版本、数据目录、磁盘容量、失效引用和最近日志；用户主目录路径会替换为 `%USERPROFILE%`。

恢复完成后需要重启应用。恢复目标必须为空，应用不会覆盖正在使用的数据目录。

数据库结构升级前，程序会在 `Backups` 子目录自动保留最近 5 个升级前备份：

```text
%LocalAppData%\QHHDesktopStorageBox\Backups\
```

## 隐私与网络

- 收纳盒数据库、普通盒文件、日志和设置全部保存在本机。
- 应用不上传文件、文件名、搜索记录、数据库、日志或使用统计。
- 网络请求仅用于连接本项目 GitHub 仓库的 Releases，以检查更新和下载用户确认的更新包。
- 更新下载只允许本项目仓库和 GitHub 官方 Release 资产域名。
- 更新包必须具有 Release 中发布的 SHA-256；校验失败时不会解压或安装。

## 卸载

可从 Windows“设置 > 应用”或开始菜单卸载。卸载程序只删除程序文件，不删除用户数据。

需要彻底清理时，请在确认不再需要数据后手动删除：

```text
%LocalAppData%\QHHDesktopStorageBox
```

如果曾在设置中迁移数据目录，还应删除当时选择的自定义数据目录。

## 已知限制

- Windows 10 的部分桌面宿主和 DPI 行为可能与 Windows 11 不同，优先支持 Windows 11。
- 映射盒和智能盒依赖源路径。移动源文件、断开网络盘或删除目录后，需要重新检查并修复引用。
- 正在被其他程序独占使用的文件可能无法移动；应用不会强制结束占用进程。
- 完整备份不会复制映射盒和智能盒指向的源文件，因为这些文件不属于应用存储目录。
- 未签名的社区构建可能触发 Windows SmartScreen。正式 Release 可配置 Authenticode 签名。

## 使用说明

- 将文件或文件夹直接拖入收纳盒即可开始使用。
- **普通收纳盒**会把文件或文件夹实际移动到 QHH Desktop Storage Box 的数据目录，适合由应用统一管理的临时文件。
- **映射收纳盒**只保存源文件的绝对路径，不移动、复制或删除源文件，适合项目目录、工作目录以及经常被其他程序使用的文件。
- **系统应用**不以文件形式搬入应用数据目录。传统程序保存可执行文件路径引用，UWP/商店应用保存 `shell:AppsFolder` 应用标识；卸载系统应用后引用可能失效。
- 建议文件夹尽量使用映射收纳盒，避免移动大量文件或正在使用的文件；需要保留文件原位置时，请优先选择映射收纳盒。
- 待办的“清单完成率”统计当前盒内所有未归档事项，不按日期筛选；归档会将已完成事项移出统计。单项删除撤销仅在应用运行期间有效，删除整个待办盒会同时删除归档历史且无法撤销。
- Windows 10 暂时可能有部分功能不兼容，建议优先使用 Windows 11。

## 技术栈

| 技术 | 说明 |
|------|------|
| .NET 10 | 运行时 |
| WPF | 原生 Windows UI |
| Win32 API | Shell 打开、全局快捷键、窗口层级 |
| SQLite | 本地持久化（WAL 模式） |
| CommunityToolkit.Mvvm | MVVM 框架 |
| TinyPinyin | 低内存中文文件名拼音搜索 |
| xUnit | 单元测试 |

本项目有意避免使用 Electron、WebView 外壳和沉重的第三方 UI 框架。

## 本次版本更新

### 新增功能

- **临时收件箱** — 先集中收集文件，再移动到其他收纳盒分类整理
- **智能收纳盒** — 根据目录、名称、扩展名和修改时间自动维护路径引用
- **多选批量操作** — 支持批量打开、导出到桌面、移动和删除
- **导入预检** — 移动前显示文件数量、总大小、跨盘、重名和占用风险
- **操作历史与撤销** — 记录导入、移动、导出和删除，并可撤销最近一次操作
- **拼音搜索** — 快捷面板支持中文全拼和拼音首字母搜索
- **盒级视觉样式** — 每个收纳盒可以独立选择和保存图标风格
- **全新用户手册** — 安装包和便携包内含中文使用说明书

### 性能与流畅度优化

- 缺少文件的磁盘扫描增加节流，避免每次刷新都遍历全部项目
- 合并盒内项目查询，减少 SQLite 查询次数
- 设置项改为批量读写，降低数据库连接与事务开销
- 快捷面板改为首次打开时按需加载，减少启动内存占用
- 快捷搜索增加防抖，并在后台线程过滤，输入时保持界面流畅
- 优化拼音搜索缓存，减少重复转换和临时对象分配
- 智能盒自动同步增加节流，避免频繁扫描相同目录
- 待办服务移除不必要的线程池包装，降低后台 CPU 使用
- 数据目录迁移的文件复制改到后台线程，避免阻塞界面
- 主控制台重载时复用已有项目 ViewModel，减少对象分配和图标重复加载
- 收纳盒设置改为按需加载，不再启动时查询全部盒子的设置
- 桌面布局保存改为批量写入，减少磁盘和数据库操作

### 界面与品牌

- 产品名称更新为 **QHH Desktop Storage Box**
- 更新程序、安装包、托盘、桌面快捷方式和关于页 Logo
- 完成工作台玻璃风格、按钮光影、选中态和列表交互优化
- 安装包、便携包和说明书统一使用新品牌名称

## 仓库结构

```text
QHHDesktopStorageBox.sln
src/
  QHHDesktopStorageBox.App/       WPF UI、窗口、视图模型、拖放、快捷键绑定
  QHHDesktopStorageBox.Core/      模型、SQLite 持久化、文件导入/删除规则、更新检查
  QHHDesktopStorageBox.Native/    Shell 打开、全局快捷键、系统托盘
tests/
  QHHDesktopStorageBox.Core.Tests/
```

## 环境要求

- Windows 10/11
- .NET SDK `10.0.300` 或兼容的 .NET 10 SDK

> Windows 10 暂时可能有部分功能不兼容，建议优先使用 Windows 11。

## 构建

```powershell
dotnet build QHHDesktopStorageBox.sln
```

也可以在仓库根目录执行快捷脚本：

```powershell
.\build.ps1
```

该脚本使用 `Release` 配置构建完整解决方案。

### 发布 Windows x64 版本

维护者可在安装 Inno Setup 6 后执行：

```powershell
.\tools\Publish-QHHDesktopStorageBox.ps1
```

脚本会生成自包含便携 ZIP、`Setup.exe` 安装包及各自的 SHA-256 校验文件。发布前应确认 ZIP 解压后可以启动，并且 GitHub Release 同时上传 ZIP 和 `Setup.exe`；不要只把单个 exe 从发布目录手工压进 ZIP。

发布目录同时包含 `QHH-Desktop-Storage-Box-User-Manual.pdf`，安装版也会在开始菜单提供“用户说明书”入口。

## 本地开发

```powershell
.\dev.ps1
```

该脚本使用 `Debug` 配置构建并启动 WPF 应用。

Debug 可执行文件位于：

```text
src/QHHDesktopStorageBox.App/bin/Debug/net10.0-windows/QHHDesktopStorageBox.App.exe
```

## 测试

```powershell
dotnet test QHHDesktopStorageBox.sln
```

CI 会在每次推送到 `main` 和 Pull Request 时执行 Release 构建及完整测试。测试覆盖默认收纳盒创建、普通/映射/像素盒导入、重复文件名后缀、跨盒移动、原位还原删除、数据库升级备份、完整备份恢复、失效引用扫描和更新 URL/校验规则。

## 运行时数据

```text
%LocalAppData%\QHHDesktopStorageBox\
  qhhdesktopstoragebox.db          SQLite 数据库
  Boxes\{BoxId}\                   普通收纳盒的文件存储
  Backups\                         数据库结构升级前备份
  logs\                            运行日志
```

## 贡献与安全

- 开发、构建和测试要求见 [CONTRIBUTING.md](CONTRIBUTING.md)。
- 版本变化见 [CHANGELOG.md](CHANGELOG.md)。
- 安全漏洞请使用 [私有漏洞报告](https://github.com/honghao919/QHHDesktopStorageBox/security/advisories/new)，不要公开提交。
- Release 发布流程位于 `.github/workflows/release.yml`，正式发布需要手动批准；配置签名密钥后会签名主程序和安装包。

## 开源协议

本项目采用**双许可**，两者均为**非商业许可**：**禁止任何商业用途**。源代码与文档/素材分别授权。

### 源代码 —— PolyForm Noncommercial License 1.0.0

`src/`、`tests/`、`tools/`、`installer/` 下的源代码、构建脚本与配置文件采用 **PolyForm Noncommercial License 1.0.0** 授权，完整条款见 [LICENSE](LICENSE)。

**允许**（非商业目的）：

- 个人使用：研究、实验、测试、个人学习、私人娱乐、爱好项目、宗教活动
- 非商业组织使用：慈善组织、教育机构、公共研究机构、公共安全或卫生机构、环保组织、政府机构（不论资金来源）
- 修改、创作新作品，以及分发副本（须随附许可条款与 `Required Notice:` 声明）

**禁止**：

- 任何商业用途

**其他要点**：

- 附带专利授权；若你书面主张本项目侵犯专利，则专利授权立即终止
- 违规后收到书面通知起 32 天内完全纠正并采取补救措施，授权可继续；否则立即终止
- 软件按「现状」提供，不附带任何担保

### 文档与素材 —— CC BY-NC-SA 4.0

`docs/` 下的文档与图片，以及 `src/QHHDesktopStorageBox.App/Assets/` 下的图标、美术等媒体素材，采用 **CC BY-NC-SA 4.0** 授权，完整说明见 [LICENSE-DOCS](LICENSE-DOCS)。

- **BY（署名）**：二次修改必须注明原作者 Thewitchcat，并标注当前项目来源 QHH Desktop Storage Box
- **NC（非商用）**：禁止商业使用
- **SA（相同方式共享）**：衍生作品必须以相同协议开源

## 作者

- **原作者**：Thewitchcat
- **当前项目维护与界面修改**：QHH
- GitHub：[honghao919/QHHDesktopStorageBox](https://github.com/honghao919/QHHDesktopStorageBox)
