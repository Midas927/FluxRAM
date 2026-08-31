# FluxRAM

[English](README.en.md) | 简体中文

FluxRAM 是一款面向 Windows 的本地内存 Boost 工具。它会在你主动执行或内存压力升高时，评估并裁剪闲置后台进程的工作集，同时避开游戏、创作软件、本地 AI、直播工具以及其他活跃或受保护的应用。

## 为什么做 FluxRAM

在内存容量有限的电脑上，浏览器、聊天软件和启动器等后台程序会逐渐占用可用内存，可能造成卡顿或应用退出。FluxRAM 专注于一个具体问题：在尽量不影响活跃应用的前提下，回收闲置后台占用。

FluxRAM 不能把 8GB 变成 16GB，也不承诺固定释放量。实际效果取决于当前程序、系统负载和保护设置；应用重新活跃后重新占用内存属于正常现象。

## 下载

可直接从以下 Release 页面下载最新版：

[国内下载（GitCode）](https://gitcode.com/Midas927/FluxRAM/releases) | [GitHub 备用下载](https://github.com/Midas927/FluxRAM/releases/latest)

推荐下载 `FluxRAM-Portable-Windows-x64.zip`，解压后双击 `FluxRAM.exe` 即可运行。普通版和 Pro 版使用同一个主程序；Pro 功能需使用当前电脑机器标识对应的 Pro Key 激活。

如果希望下载包更小，也可以选择 `FluxRAM-Lite-Windows-x64.zip`。Lite 版功能相同，但要求电脑已经安装 `.NET 8 Desktop Runtime`；若不确定，选择 Portable 版。

## 实际演示

下面是 FluxRAM 与 Windows 任务管理器一起使用的实际截图。不同电脑、不同后台进程数量和当前负载都会影响释放结果，截图只作为界面和效果参考。

<p>
  <img src="docs/assets/fluxram-demo-main.jpg" alt="FluxRAM 主界面与任务管理器内存变化演示" width="900">
</p>

<p>
  <img src="docs/assets/fluxram-demo-details.jpg" alt="FluxRAM 详细界面与任务管理器内存变化演示" width="900">
</p>

## 核心功能

| 功能 | FluxRAM | FluxRAM Pro |
| --- | --- | --- |
| 立即 Boost | 支持 | 支持 |
| 内存压力升高后自动 Boost | 支持 | 支持 |
| Daily / Gaming 档位 | 支持 | 支持 |
| 极致性能档位 | 不支持 | 支持 |
| 深度释放与后台服务建议 | 不支持 | 支持 |
| 托盘常驻与托盘 Boost | 支持 | 支持 |
| 添加 / 删除受保护应用 | 支持 | 支持 |
| 从正在运行的进程里选择保护应用 | 支持 | 支持 |
| 按进程名保护应用 | 支持 | 支持 |
| 按 EXE 完整路径保护 | 不支持 | 支持 |
| 子进程关联保护 | 不支持 | 支持 |
| 窗口识别保护 | 不支持 | 支持 |

## FluxRAM 普通版

普通版包含完整的日常 Boost 流程：

1. `Boost Now`：需要时立即执行一次内存 Boost。
2. `Auto Boost`：开启后在内存压力高时自动触发。
3. `Daily` / `Gaming`：两个稳妥档位，适合大多数办公、游戏、本地 AI 和创作场景。
4. 应用保护列表：可以添加、删除，也可以直接从正在运行的进程里选择。
5. 托盘体验：最小化后继续待命，可从托盘执行 Boost。
6. 可见指标：显示内存变化、可用内存、最近裁剪量、累计裁剪量、净收益和程序自身开销。

## FluxRAM Pro

Pro 面向更重的本地负载和更精细的保护需求：

1. `Extreme` 极致性能档位。
2. 深度释放：汇总零碎后台进程、识别闲置状态并由用户确认关闭。
3. 可选后台服务分类与关闭建议。
4. 按 EXE 完整路径保护，减少同名进程误判。
5. 子进程与关联应用的智能保护。
6. 当前电脑永久激活，激活后无需联网验证。

## Pro 激活方式

FluxRAM Pro 需要使用与当前电脑机器标识绑定的 Pro Key 激活：

1. 打开 FluxRAM。
2. 在界面中复制当前电脑的机器标识。
3. 按购买渠道的提示提交机器标识。
4. 收到专属 Pro Key 后，在 FluxRAM 中输入并激活。
5. Pro Key 绑定当前电脑；更换电脑后需要重新生成。

Pro Key 根据机器标识生成，仅适用于对应电脑。

## 购买入口

在程序的“版本”问号说明中点击“升级 Pro”：

- 简体中文界面显示支付宝收款码和付款流程，价格为人民币 10 元。
- 其他语言界面会打开 Whop 购买页面，价格为 3 美元。

## 本地与安全

1. FluxRAM 在本机执行，不依赖云端遥测。
2. Boost 过程主要裁剪冷后台进程的 working set。
3. 默认策略不会停止系统服务。
4. 受保护应用会从候选列表中排除，降低误伤概率。

## 文档

- [商业说明书](docs/FluxRAM-Commercial-Manual.zh-CN.md)
- [Commercial Manual](docs/FluxRAM-Commercial-Manual.en.md)
- [IT 技术说明书](docs/FluxRAM-IT-Technical-Manual.zh-CN.md)
- [IT Technical Manual](docs/FluxRAM-IT-Technical-Manual.en.md)
