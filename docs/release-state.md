# FluxRAM Release State

## 当前版本线

- 当前正式版：0.3.7
- 当前测试版：0.3.8-beta.1
- 下一测试版：待定
- 下一正式版：待定
- 发布分支：main

## 0.3.6-beta.1 已纳入

- 开机 Auto Boost 启动时静默驻留托盘，不弹主窗口。
- 新增 Gaming 档位，面向游戏 PC 和 Windows 掌机。
- Gaming 档位启用内置游戏/掌机相关进程保护名单。
- 无候选或无裁剪时显示更明确原因。
- 保留 0.3.5 的详细界面滚动修复和滚动条加粗。

## 0.3.6 正式版已纳入

- 模式收敛为 Daily / Gaming / Extreme。
- 新安装默认选择 Gaming。
- 旧 Standard / Balanced 设置迁移到 Gaming。
- 档位说明窗口支持滚动和调整大小，内容过长时不再卡住。
- 手动 Boost / Preview 比 Auto Boost 更积极，Auto Boost 继续保持保守。
- 新增 Extreme Close：用户主动确认后关闭高占用应用。
- Extreme Close 可列出前台高占用应用，但默认不勾选前台项。
- 受保护应用列表限制显示高度，列表内优先响应滚轮，到边界后继续滚动详情页。
- 内存指标中的自身开销改为整行换行显示，不再截断。
- Extreme Close 排除 Windows 系统进程白名单，避免提供高风险关闭候选。

## 0.3.7 正式版已纳入

- 调整版本功能展示：Extreme Close 归入 Pro，并同步程序界面与产品说明。
- 同步普通版 / Pro 版本说明，明确 Extreme Close、Extreme 档位和高级应用保护属于 Pro。
- 应用、官网和更新检测版本统一为 0.3.7。
- 自动化测试 81/81 通过，Lite / Portable 发布包构建成功，EXE FileVersion 为 0.3.7.0。

## 0.3.8-beta.1 已纳入

- 检查更新升级为应用内下载、SHA256 校验、替换和重启流程。
- 更新成功启动后自动清理旧 EXE 与本次更新缓存；替换失败时保留原程序。
- Lite / Portable 发布包写入分发模式，更新时自动选择对应包。
- Beta 输出到 `dist/beta` 并带完整预发布版本文件名，不覆盖正式版本地资产。
- 预发布版本参与更新比较，Beta 可以正确识别后续 Beta 与正式版。
- 正式版只读取正式 Release，Beta 通道同时读取预发布与正式 Release。
- Extreme Close 更名为 Deep Release / 深度释放，仍由用户选择并确认要关闭的应用。
- 精确路径、子进程与关联窗口统一为“智能关联保护”，普通 Boost 与深度释放共用同一套保护判断。
- 收紧关联窗口判断，浏览器页面标题不再触发关联保护。
- Pro 界面显示每次实际保护的进程数量和命中类型。

## 已确认用户反馈

- 开机自启弹主窗口，用户感知像广告。
- 32 GB 内存玩网游时 Standard 档位可能没有裁剪量，用户感知为“没效果”。
- 详细界面最近活动列表堆叠、滚动条过小、滚轮焦点不符合预期。
- 档位说明的问号弹窗内容过长时不能滚动。
- 用户会拿 FluxRAM 和 Mem Reduct、Process Lasso 这类工具比较。
- 用户希望有强力关闭高占用应用的能力，但普通 Boost 不能自动关前台应用。

## 0.3.6 正式版发布验证

- `dotnet test FluxRAM.sln` 通过：80/80。
- Lite / Portable 正式包已重新生成并附 SHA256 校验文件。
- exe 文件版本为 `0.3.6.0`。
- Release Notes 使用中文说明 0.3.5 到 0.3.6 的重点变化。
- GitHub Release `v0.3.6` 标记为 Latest。
- 程序内 Check Updates 可识别 `v0.3.6`。

## 暂不做

- 不拆 PC 版 / 掌机版两个安装包。
- 不做 Process Lasso 式复杂进程调度器。
- 不让普通 Boost 自动关闭应用。
- 不默认勾选前台应用进行 Extreme Close。
