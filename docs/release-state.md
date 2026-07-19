# FluxRAM Release State

## 当前版本线

- 当前正式版：0.3.5
- 下一正式版：0.3.6
- 下一测试版：0.3.6-beta.1
- 当前工作分支：codex/product-polish-update

## 0.3.6-beta.1 已纳入

- 开机 Auto Boost 启动时静默驻留托盘，不弹主窗口。
- 新增 `Gaming / Handheld` 档位。
- Gaming / Handheld 档位启用内置游戏/掌机相关进程保护名单。
- 无候选或无裁剪时显示更明确原因。
- 保留 0.3.5 的详细界面滚动修复和滚动条加粗。

## 已确认用户反馈

- 开机自启弹主窗口，用户感知像广告。
- 32 GB 内存玩网游时 Standard 档位可能没有裁剪量，用户感知为“没效果”。
- 详细界面最近活动列表堆叠、滚动条过小、滚轮焦点不符合预期。
- 用户会拿 FluxRAM 和 Mem Reduct、Process Lasso 这类工具比较。

## 0.3.6 正式版发布前检查

- `dotnet test FluxRAM.sln` 通过。
- 本地 Lite / Portable 包重新生成。
- exe 文件版本更新为 `0.3.6.0`。
- Release Notes 用中文说明 0.3.5 到 0.3.6 的重点变化。
- 确认 GitHub Release 标记为 Latest。
- 确认程序内 Check Updates 能看到最新正式版。

## 未决定事项

- 是否在 Pro 版中加入更专业的进程保护规则模板。
- 是否新增“Game Prep”独立按钮，而不是只通过档位表达。
- 是否把 0.3.6-beta.1 公开给用户下载，还是只发给反馈用户内测。
