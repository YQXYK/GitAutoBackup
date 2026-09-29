# GitAutoBackup

> 一个图形化的 Git 备份工具：选择项目文件夹，自动备份到 GitHub 仓库。

不用记 `git` 命令，也不用手写 `.gitignore` —— 选好文件夹、点一下按钮，项目就自动提交并推送到 GitHub。

## 功能特性

- **两种备份方式**
  - **集中式**：多个项目备份到同一个仓库的不同子目录，适合统一管理
  - **独立式**：每个项目各自推送到一个独立仓库
- **内置登录**：软件内点一下、浏览器授权即可登录 GitHub，**无需安装 gh CLI**
- **自定义排除规则**：全局规则 + 按项目规则，跳过 `node_modules`、`bin`、`obj` 等目录（含内置常用规则）
- **备份前体积检查**：自动统计待备份内容的体积，发现超过 GitHub 单文件上限（100 MB）的文件时提前告知，可由你决定跳过或取消，避免"提交半天才在推送阶段失败"
- **失败原因通俗化**：备份失败时不丢给你一串 git 英文报错，而是翻译成「发生了什么 + 为什么 + 怎么办」，并标明该怎么处理；原始信息折叠在下方，需要时再复制给开发者
- **定时自动备份**：按分钟 / 小时 / 天自动执行
- **GitHub 仓库管理**：查看账号下所有仓库、修改可见性（公开/私有）与描述、删除仓库
- **自动生成仓库说明**：备份仓库的 `README.md` 自动生成并更新（含仓库类型与项目列表）
- **日志 / 终端 / 任务三个面板**：底部可切换，操作进度与程序内部细节分开呈现
- **日志自动落盘**：程序日志按天写入本地文件（保留 30 天、超出自动分片），出问题可回溯
- **内置终端**：直接在内置终端里执行 `git` / `gh` 命令
- **环境自检**：启动时检测 `git` / `gh`，缺失时给出官方下载入口

## 环境要求

| 依赖 | 说明 |
|---|---|
| Windows | 10 / 11（x64） |
| [git](https://git-scm.com/download/win) | **必需**，备份的提交与推送通过它完成 |
| [GitHub CLI (gh)](https://cli.github.com/) | **可选**。软件支持内置登录（浏览器授权），未安装 gh 也能正常使用 |
| .NET 10 Desktop Runtime | 仅"框架依赖"版本需要；**自包含版本无需安装** |

## 下载

前往 [Releases](../../releases) 页面下载最新版本：

- `GitAutoBackup.exe` —— **自包含单文件版**，双击即可运行，无需安装 .NET

> 首次运行若出现 Windows SmartScreen 提示，点「更多信息」→「仍要运行」即可（程序未做代码签名）。

## 使用说明

1. **登录 GitHub**：点左下角**账号入口**（头像）→「登录 GitHub ...」，在浏览器中输入设备码完成授权；若本机已用 `gh auth login` 登录，软件也会自动识别
2. **新增备份**
   - 集中式：前往「集中仓库」页新建仓库 → 选择文件夹加入
   - 独立式：前往「独立仓库」页填写仓库名 → 选择文件夹加入
3. **执行备份**：回到「备份任务」页，勾选要备份的项目 → 点「备份选中项目」
4. **解锁删除权限**（可选）：删除 GitHub 仓库需要 `delete_repo` 权限，点左下角账号入口 →「解锁删除权限」完成授权

## 关于 GitHub 的体积限制

GitHub 对仓库里的文件有硬性限制，超限的推送会**直接失败**。软件会在每次备份前自动检查并提示：

| 限制项 | 数值 | 超限后果 |
|---|---|---|
| 单个文件（警告线） | 50 MiB | 推送时 Git 给警告，仍可推上去，但仓库会明显膨胀 |
| 单个文件（硬上限） | 100 MiB | **推送被 GitHub 拒绝**（`file is too large`），必须用 Git LFS |
| 仓库总体积 | 建议 < 1 GB，强烈建议 < 5 GB | 过大时 clone / push 极慢，可能被官方要求整改 |
| 单次推送总量 | 2 GB | 硬限制 |

检出超过 100 MB 的文件时，软件会列出清单并让你选择**跳过并继续**或**取消备份**。选择跳过时：

- **集中式**：这些文件不会被复制进备份仓库，源项目不受任何影响；
- **独立式**：会在源项目的 `.gitignore` 中忽略它们，并从 git 索引移除（**本地文件不会删除**）。

> 注意：如果超大文件在**更早的提交中已经入库**，仅忽略索引不足以让推送成功（历史中的大对象仍在）。这种情况需要用 `git filter-repo` 清理历史，或改用 Git LFS。

## 从源码构建

```bash
# 调试运行
dotnet run

# 发布（框架依赖）
dotnet publish -c Release -o publish

# 发布（自包含单文件，免装 .NET）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

要求 .NET 10 SDK。

## 项目结构

```
Models/       数据模型（全局配置、备份任务、排除规则、GitHub 仓库）
Services/     核心服务
  ProcessRunner     子进程执行封装
  GitService        git 命令封装
  GitHubService     GitHub 操作统一入口（优先 REST API，回退 gh CLI）
  GitHubAuthService OAuth 设备码登录 + token 本地加密存储（DPAPI）
  GitHubApiClient   GitHub REST API 直连
  AvatarService     账号头像下载与缓存
  BackupService     备份流程编排（集中式 / 独立式）
  SettingsService   配置持久化
  LogService        日志落盘（按天滚动、限长分片、过期清理）
  PathService       统一的用户数据目录
  BackupSizeChecker 备份前体积检查（识别超出 GitHub 上限的文件）
  ErrorTranslator   把 git/gh 的原始报错翻译成通俗说明
Behaviors/    附加行为（如表格滚轮事件转发）
ViewModels/   MVVM 视图模型
Views/        WPF 界面
Assets/       应用图标（app.ico 多尺寸 / app-512.png 大图，供仓库配图）
tools/        构建脚本（make_icon.py 由源图生成多尺寸图标）
```

### 数据目录

所有用户数据都在 `%AppData%\GitAutoBackup\`，删除该目录即可完全清理（无注册表残留）：

| 文件 | 内容 |
|---|---|
| `settings.json` | 配置：仓库名、备份任务、排除规则、定时设置 |
| `token.dat` | GitHub 登录令牌，经 **DPAPI 加密**（仅当前 Windows 用户可解，拷到别的电脑无效） |
| `avatar.png` / `avatar.login` | 头像缓存及其归属账号 |
| `logs\yyyy-MM-dd.log` | **程序日志**：按天一个文件，保留最近 30 天，单文件超过 5 MB 自动分片 |
| `errors.log` | 崩溃异常摘要（与 `logs` 内容对应，便于快速定位） |

## 技术栈

- C# / WPF / .NET 10
- **内置 GitHub OAuth 设备码登录 + REST API**；未登录时回退本机 `gh` CLI
- 提交与推送通过本机 `git` 完成（推送时携带访问令牌，确保与建仓使用同一账号）

## 第三方依赖

- [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl)（MIT）—— 内置终端控件，基于官方 Windows Terminal 的 ConPTY 后端。注意其依赖 `Microsoft.Windows.Console.ConPTY` 与 `CI.Microsoft.Terminal.Wpf` 的 **beta 版本**。

## 许可证

[MIT](LICENSE)

## 作者

[YQXYK](https://github.com/YQXYK)
