# FluxRAM IT 技术说明书

文档版本：1.1
更新日期：2026-05-07
适用对象：IT 管理员、桌面工程团队、端点安全团队、开发维护者

## 1. 技术定位

FluxRAM 是 Windows 11 本地内存 Boost 工具，基于 C# / .NET 8 / WPF 开发。

核心目标：

1. 通过裁剪冷后台进程 working set 缓解内存压力。
2. 提供 `Light`、`Standard`、`Extreme Performance` 三档策略。
3. 以本地执行为主，不内置云端遥测上传。
4. 支持托盘常驻、手动 Boost 和压力触发的 Auto Boost。

非目标：

1. 不是内核驱动。
2. 不是长期系统服务调优器。
3. 不替代企业端点管理策略。
4. 不承诺固定回收比例，实际效果取决于当前系统负载和进程状态。

## 2. 目录结构

```text
src/FluxRAM.App        WPF 桌面应用、UI、授权、托盘和运行时编排
src/FluxRAM.Core       内存快照、进程枚举、策略规划、Win32 调用
tools/FluxRAM.Keygen   内部 Pro key 生成器
tests/FluxRAM.*        单元测试
scripts/               发布、授权 key、运行时安装脚本
docs/                  商业说明书和 IT 技术说明书
```

## 3. 权限与安全模型

1. 主程序通过 `app.manifest` 请求 `requireAdministrator`。
2. 提权原因是 working set 裁剪和部分进程访问在普通权限下会失败。
3. 程序不会向远程进程注入代码。
4. 程序不会默认写入开机启动项。
5. 正常 Boost 流程不调用云端 API。
6. 运行时安装脚本只在缺少 .NET Desktop Runtime 时下载官方安装包。

授权安全：

1. FluxRAM Pro 使用机器 ID + RSA 签名 key 激活。
2. 应用内只嵌入公钥用于验签。
3. 私钥必须保存在仓库外，不能上传 GitHub，也不能发给客户。
4. Keygen 只用于内部生成 Pro key。

## 4. 版本与功能开关

| 功能 | FluxRAM | FluxRAM Pro |
| --- | --- | --- |
| Light / Standard | 支持 | 支持 |
| Extreme Performance | 不支持 | 支持 |
| Boost Now | 支持 | 支持 |
| Auto Boost | 支持 | 支持 |
| 托盘 Boost | 支持 | 支持 |
| 保护应用添加 / 删除 | 支持 | 支持 |
| 从运行进程选择保护应用 | 支持 | 支持 |
| 按进程名保护 | 支持 | 支持 |
| 按 EXE 路径保护 | 不支持 | 支持 |
| 子进程关联保护 | 不支持 | 支持 |
| 窗口识别保护 | 不支持 | 支持 |

公开交付只使用一个 `FluxRAM.exe`。程序默认以普通版启动，Pro 功能必须通过当前机器 ID 绑定的 Pro key 激活，不能通过公开构建参数直接生成内置 Pro 版。

## 5. Boost 执行流程

触发方式：

1. `Boost Now`：用户点击后执行一次。
2. `Auto Boost`：轻量监控内存压力，达到当前档位阈值且冷却完成后触发。
3. 托盘 `Boost Now`：从系统托盘菜单执行一次。

单次流程：

1. 使用 `GlobalMemoryStatusEx` 读取内存状态。
2. 枚举进程并采样 CPU、窗口、前台和内存信息。
3. 用 `PurgePolicyService` 生成候选列表。
4. 排除系统白名单、前台进程、冷却中进程和受保护应用。
5. 使用 `SetProcessWorkingSetSize(-1, -1)` 与 `EmptyWorkingSet` 裁剪候选进程。
6. 更新最近裁剪量、累计裁剪量、净收益、回弹率、自身开销和最近事件。

## 6. 当前策略矩阵

| 参数 | Light | Standard | Extreme Performance |
| --- | ---: | ---: | ---: |
| MaxPurgeTargetsPerPass | 2 | 5 | 0，表示全部合格候选 |
| MinimumCandidateWorkingSetBytes | 280 MB | 160 MB | 64 MB |
| PurgeWhenAvailableMemoryBelowBytes | 5 GB | 9 GB | 0 |
| PurgeWhenAvailableMemoryBelowPercentOfTotal | 26% | 40% | 0 |
| IgnoreMemoryPressureThreshold | false | false | true |
| AllowForegroundProcessPurge | false | false | true |
| ProcessCooldownSeconds | 60 | 24 | 0 |
| NormalIntervalSeconds | 8 | 5 | 1 |
| BackoffIntervalSeconds | 18 | 12 | 1 |
| LowYieldThresholdBytes | 96 MB | 40 MB | 0 |
| MinimumColdnessScore | 65 | 55 | 20 |
| BoostCooldownSeconds | 120 | 120 | 120 |
| EnablePriorityAdjustment | false | false | false |
| EnableServiceKiller | false | false | false |

说明：

1. `Extreme Performance` 只在 FluxRAM Pro 中开放。
2. 所有默认档位都不停止系统服务。
3. `Extreme Performance` 更激进，建议先在受控机器上验证。

## 7. 保护应用规则

普通版保护：

1. 用户可通过 EXE 文件或正在运行的进程添加保护应用。
2. 用户可从列表中删除保护项。
3. 运行时按进程名跳过受保护应用。

Pro 高级保护：

1. 精确 EXE 路径匹配。
2. 子进程关联识别。
3. 可见窗口标题识别。

保护列表存储在公共应用数据目录下，由 `ProtectedAppsStore` 负责读写。

## 8. 打包与分发

构建要求：

1. Windows 11。
2. .NET 8 SDK，包含 Windows Desktop workload。

发布两个版本：

```powershell
.\scripts\publish-win-x64.ps1
```

输出：

```text
dist\fluxram-win-x64\FluxRAM.exe
```

Small 模式为默认模式，依赖目标机器安装 .NET 8 Desktop Runtime。若要包含运行时：

```powershell
.\scripts\publish-win-x64.ps1 -Mode Portable
```

## 9. 目标机器运行时

Small 包可搭配：

```bat
scripts\install-dotnet-desktop-runtime-8.bat
scripts\run-fluxram.bat
```

脚本行为：

1. 检测 `Microsoft.WindowsDesktop.App` 8.x。
2. 必要时请求 UAC。
3. 优先使用同目录离线安装包。
4. 在线时从官方 .NET release metadata 获取安装地址。
5. 安装日志写入 `%TEMP%\fluxram-dotnet-runtime-install.log`。

## 10. 内部授权工具

生成密钥对：

```powershell
.\scripts\new-license-keypair.ps1
```

命令行生成 Pro key：

```powershell
.\scripts\generate-pro-key.ps1 -MachineId "FLX-...." -PrivateKeyXmlPath "D:\secure\fluxram-license.private-key.xml"
```

发布内部 Keygen：

```powershell
.\scripts\publish-keygen-win-x64.ps1
```

输出：

```text
dist\keygen-win-x64\FluxRAM-Keygen.exe
```

安全要求：

1. `fluxram-license.private-key.xml` 只能在内部安全位置保存。
2. 不要把 `.secrets`、私钥或 Keygen 输出上传 GitHub。
3. 公开下载包只通过 GitHub Release 附件分发，不提交到仓库源码树。
4. 生产发布前应替换应用内公钥，并对发行 exe 做代码签名。

## 11. Win32 API 范围

内存和进程：

1. `OpenProcess`
2. `SetProcessWorkingSetSize`
3. `EmptyWorkingSet`
4. `GetProcessMemoryInfo`
5. `CloseHandle`
6. `GlobalMemoryStatusEx`
7. `GetForegroundWindow`
8. `GetWindowThreadProcessId`

服务控制接口目前保留在代码中，但默认档位不启用服务停止。

## 12. 验收清单

1. `dotnet test FluxRAM.sln` 全部通过。
2. 普通版只能选择 `Light` 和 `Standard`。
3. 输入当前机器 ID 对应的有效 Pro key 后，可选择 `Extreme Performance`。
4. 普通版可添加、删除保护应用，可从运行进程添加。
5. Pro 版高级保护说明显示正确。
6. `Boost Now` 可执行并刷新指标。
7. `Auto Boost` 开启后只在压力达到阈值和冷却完成时触发。
8. 最小化、关闭到托盘、托盘恢复、托盘退出都可用。
9. GitHub 仓库内不包含 `dist`、`bin`、`obj`、`.secrets`、私钥和日志。
