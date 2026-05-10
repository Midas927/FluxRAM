# FluxRAM

[English](README.en.md) | 简体中文

FluxRAM 是一款面向 Windows 的本地内存 Boost 工具。它的目标不是一直在后台疯狂清理，而是在你需要的时候快速释放冷后台进程占用，并在内存压力升高时自动处理，同时尽量避开游戏、创作软件、本地 AI、直播工具等不该被碰的应用。

## 为什么做 FluxRAM

这段时间 DIY 市场里内存价格压力很明显，很多 8GB 用户明明想升级，却又因为预算问题迟迟没有下手，只能继续忍受卡顿的电脑。我自己的笔记本也只有 12GB，经常遇到浏览器被内存压力挤到闪退，所以做了 FluxRAM。

FluxRAM 不是把 8GB 变成 16GB 的魔法。它更现实一点：尽量清掉那些占着内存却暂时不干活的“内存刺客”，让你现有的内存更耐用一些。实际效果取决于你正在运行什么程序；在常见办公、浏览器、聊天软件和启动器场景下，通常可能释放几百 MB 到 1-2GB，后台进程特别多时可能更多，但应用重新活跃后内存回弹也很正常。

希望在内存价格让人犹豫升级的今天，FluxRAM 能给大家带来一个方便、简单、不会打扰正常工作的 Windows 小工具。因为我是个人开发者，Pro 激活 Key 目前都是根据机器码手动签发，购买后请稍许等待。

## 下载

不会使用 Git 的用户，直接从这里下载最新版：

[下载 FluxRAM 最新版](https://github.com/Midas927/FluxRAM/releases/latest)

推荐下载 `FluxRAM-Portable-Windows-x64.zip`，解压后双击 `FluxRAM.exe` 即可运行。普通版和 Pro 版使用同一个主程序；Pro 功能只能通过当前电脑的机器码 Key 激活。

如果你希望安装包更小，也可以下载 Small 版。Small 版功能不缩水，但需要当前电脑已安装 `.NET 8 Desktop Runtime`。

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
| 轻量 / 标准档位 | 支持 | 支持 |
| 极致性能档位 | 不支持 | 支持 |
| 托盘常驻与托盘 Boost | 支持 | 支持 |
| 添加 / 删除受保护应用 | 支持 | 支持 |
| 从正在运行的进程里选择保护应用 | 支持 | 支持 |
| 按进程名保护应用 | 支持 | 支持 |
| 按 EXE 完整路径保护 | 不支持 | 支持 |
| 子进程关联保护 | 不支持 | 支持 |
| 窗口识别保护 | 不支持 | 支持 |

## FluxRAM 普通版

普通版已经覆盖日常使用的大部分体验：

1. `Boost Now`：需要时立即执行一次内存 Boost。
2. `Auto Boost`：开启后在内存压力高时自动触发。
3. `Light` / `Standard`：两个稳妥档位，适合大多数游戏、办公、本地 AI 和创作场景。
4. 应用保护列表：可以添加、删除，也可以直接从正在运行的进程里选择。
5. 托盘体验：最小化后继续待命，可从托盘执行 Boost。
6. 可见指标：显示内存变化、可用内存、最近裁剪量、累计裁剪量、净收益和程序自身开销。

## FluxRAM Pro

Pro 面向更重的本地负载和更精细的保护需求：

1. `Extreme Performance` 极致性能档位。
2. 按 EXE 完整路径保护，减少同名进程误判。
3. 子进程关联保护，适合启动器、游戏客户端、本地 AI 工具链和创作软件。
4. 窗口识别保护，适合进程名不稳定或被包装启动的应用。
5. 当前电脑永久激活，激活后无需联网验证。

## Pro 激活方式

FluxRAM Pro 只能通过机器码 Key 激活：

1. 打开 FluxRAM。
2. 在界面中复制当前电脑的机器标识。
3. 将机器标识发给销售或客服。
4. 收到专属 Pro Key 后，在 FluxRAM 中输入并激活。
5. Key 绑定当前电脑；更换电脑后需要重新生成。

Pro Key 由官方根据机器标识签发，绑定当前电脑使用。

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
