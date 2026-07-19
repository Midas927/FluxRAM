# FluxRAM Release State

## 当前版本线

- 当前正式版：0.3.5
- 当前测试版：0.3.6-beta.1
- 下一测试版：0.3.6-beta.2
- 下一正式版：0.3.6
- 当前工作分支：codex/0.3.6-product-upgrade

## 0.3.6-beta.1 已纳入

- 开机 Auto Boost 启动时静默驻留托盘，不弹主窗口。
- 新增 Gaming 档位，面向游戏 PC 和 Windows 掌机。
- Gaming 档位启用内置游戏/掌机相关进程保护名单。
- 无候选或无裁剪时显示更明确原因。
- 保留 0.3.5 的详细界面滚动修复和滚动条加粗。

## 0.3.6-beta.2 正在纳入

- 模式收敛为 Daily / Gaming / Extreme。
- 新安装默认选择 Gaming。
- 旧 Standard / Balanced 设置迁移到 Gaming。
- 档位说明窗口支持滚动和调整大小，内容过长时不再卡住。
- 手动 Boost / Preview 比 Auto Boost 更积极，Auto Boost 继续保持保守。
- 新增 Extreme Close：用户主动确认后关闭高占用应用。
- Extreme Close 可列出前台高占用应用，但默认不勾选前台项。

## 已确认用户反馈

- 开机自启弹主窗口，用户感知像广告。
- 32 GB 内存玩网游时 Standard 档位可能没有裁剪量，用户感知为“没效果”。
- 详细界面最近活动列表堆叠、滚动条过小、滚轮焦点不符合预期。
- 档位说明的问号弹窗内容过长时不能滚动。
- 用户会拿 FluxRAM 和 Mem Reduct、Process Lasso 这类工具比较。
- 用户希望有强力关闭高占用应用的能力，但普通 Boost 不能自动关前台应用。

## 0.3.6 正式版发布前检查

- `dotnet test FluxRAM.sln` 通过。
- 本地 Lite / Portable 包重新生成。
- exe 文件版本更新为 `0.3.6.0`。
- Release Notes 用中文说明 0.3.5 到 0.3.6 的重点变化。
- 确认 GitHub Release 标记为 Latest。
- 确认程序内 Check Updates 能看到最新正式版。

## 暂不做

- 不拆 PC 版 / 掌机版两个安装包。
- 不做 Process Lasso 式复杂进程调度器。
- 不让普通 Boost 自动关闭应用。
- 不默认勾选前台应用进行 Extreme Close。
