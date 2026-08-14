# Agent 提醒器（AgentNotifier）

> 🐟 **内置默认横幅图：「吃白饭的蓝色大肥鱼」** —— DeepSeek 系列模型（deepseek-v4-flash / deepseek-v4-pro）的弹窗默认图片

为 **DeepSeek Web Harness / Claude Code** 提供提醒服务：需要用户介入 → 音效 + 弹窗；任务完成 → 另一音效 + 弹窗。Windows 桌面常驻工具，.NET 8 + WPF，零外部 NuGet 依赖。

## 弹窗长这样

![吃白饭的蓝色大肥鱼（DeepSeek 默认弹窗横幅）](docs/fish.png)

上面这张**吃白饭的蓝色大肥鱼**就是 DeepSeek 系列模型的默认弹窗横幅：识别到信号来自 `deepseek-v4-flash` / `deepseek-v4-pro` 时，弹窗顶部自动显示它（可在「通知」页为每个模型换成你自己的图片）。

## 功能

- **双端接入**：Claude Code（官方 hooks，一键接入/预览/回滚）；DSH（WebSocket 事件流 + 本机 RPC，零配置自动监听）
- **应用内富通知弹窗**：消息类型徽标（选择 / 权限 / 提交结果）+ 来源 Agent 标注 + 自定义图片与内容模板
- **模型样式（软件端自动识别）**：自动识别 DSH 会话所用模型，每个模型可完全自定义显示名、颜色、图片、弹窗标题与回复内容
- **内置默认横幅图**：DeepSeek 模型（v4-flash / v4-pro）默认使用「吃白饭的蓝色大肥鱼」，随程序打包（`builtin:fish`），无外部文件依赖
- 双事件音频（内置 8 款 + 自定义导入 WAV/MP3/FLAC）；去抖、勿扰时段、一键静音；深色模式；托盘常驻；开机自启
- 通知样式可选：应用内弹窗（默认）/ 系统 Toast / 原生气泡

## 运行

双击 `dist\AgentNotifier.exe` 即可（常驻系统托盘，关闭窗口 = 最小化到托盘）。

> 依赖：Windows 10/11 + [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

## 使用

- DSH（DeepSeek Web Harness）：无需配置，自动监听其 WebSocket 事件流与本机 RPC，识别每个会话所用模型
- Claude Code：「接入」页一键写入官方 hooks（自动备份，可预览/回滚），上报需介入/完成事件
- 「通知」页：为每个模型定制弹窗样式（显示名/颜色/图片/标题/内容模板），支持占位符
  `{agent}` `{tool}` `{type}` `{model}` `{modelName}` `{session}` `{summary}` `{time}` `{title}`
- 数据目录：`%APPDATA%\AgentNotifier\`（config.json、logs、自定义音频）

## 目录结构

```
AgentNotifier/
├── src/                  # 源码（.NET 8 + WPF）
│   ├── AgentNotifier.App/      # 主程序（窗口、弹窗 UI、内置横幅图）
│   ├── AgentNotifier.Core/     # 事件监听、模型自动识别、配置
│   ├── AgentNotifier.Audio/    # 音效合成与播放
│   ├── AgentNotifier.Notify/   # 托盘 / 系统通知
│   ├── AgentNotifier.Tools/    # Claude Code 接入向导
│   └── AgentNotifier.Smoke/    # 自测程序
├── scripts/              # 构建 / 发布 / 日志脚本
├── docs/                 # 文档素材（含默认横幅图「吃白饭的蓝色大肥鱼」）
├── dist/                 # 编译好的发行版（免安装，直接运行）
├── 需求文档.md           # 需求与设计决策记录
├── 开发日志/             # 每日开发记录
├── LICENSE               # MIT
└── README.md
```

## 构建

```
powershell -File scripts/build.ps1                 # Debug/Release 编译
powershell -File scripts/publish-dist.ps1          # 生成多文件发行版到 dist/
powershell -File scripts/publish.ps1 -SelfContained  # 生成单文件自包含版（需网络下载运行时包）
```

要求：Windows 10/11 + .NET 8 SDK。

## 卸载

运行 `dist\uninstall.ps1`：删除写入的 hooks、还原备份、清理辅助文件。

## License

MIT