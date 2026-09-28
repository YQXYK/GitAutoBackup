# GitAutoBackup

> 一个图形化的 Git 备份工具：选择项目文件夹，自动备份到 GitHub 仓库。

不用记 `git` 命令，也不用手写 `.gitignore` —— 选好文件夹、点一下按钮，项目就自动提交并推送到 GitHub。

## 功能特性

- **两种备份方式**
  - **集中式**：多个项目备份到同一个仓库的不同子目录，适合统一管理
  - **独立式**：每个项目各自推送到一个独立仓库
- **内置登录**：软件内点一下、浏览器授权即可登录 GitHub，**无需安装 gh CLI**
- **自定义排除规则**：全局规则 + 按项目规则，跳过 `node_modules`、`bin`、`obj` 等目录（含内置常用规则）
- **定时自动备份**：按分钟 / 小时 / 天自动执行
- **GitHub 仓库管理**：查看账号下所有仓库、修改可见性（公开/私有）与描述、删除仓库
- **自动生成仓库说明**：备份仓库的 `README.md` 自动生成并更新（含仓库类型与项目列表）
- **日志 / 终端 / 任务三个面板**：底部可切换，操作进度与程序内部细节分开呈现
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
Behaviors/    附加行为（如表格滚轮事件转发）
ViewModels/   MVVM 视图模型
Views/        WPF 界面
```

配置与错误日志位于 `%AppData%\GitAutoBackup\`（`settings.json` / `errors.log`），登录令牌经 DPAPI 加密后存于同目录。

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
