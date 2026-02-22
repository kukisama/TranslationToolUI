# 译见 Pro：补充说明（面向维护者/发布者）

这份文档用来承载 README 不必展开的细节：依赖、平台取舍、构建期工具、内容更新方式、发布建议等。

## 这是什么

译见 Pro 是一个主要展示 Azure Speech 能力的桌面工具：

- 采集音频（麦克风 / 系统回环 Loopback）
- 推送到 Azure Speech 做识别与翻译
- 实时显示中间结果与最终结果
- 可选同步录音，停止后转 MP3（Windows）

## 功能概览

- 实时识别/翻译（含中间结果/最终结果）
- 输入源选择：默认麦克风 / WASAPI 设备选择 / WASAPI Loopback
- 浮动字幕（可置顶）
- 会话录音：写 WAV，停止后异步转 MP3（Windows Media Foundation）
- 历史记录：按会话积累文本
- 配置持久化：本地 JSON 保存，启动自动加载

## 平台说明

- WASAPI Loopback 是 Windows 能力：把“系统正在播放的声音”作为输入源
- WAV → MP3 转码依赖 Windows Media Foundation 的编码器能力；不同 Windows 版本/精简系统可能存在差异
- 代码整体按跨平台习惯组织，但目前主要在 Windows 上验证；其它平台请先测试关键链路

## 运行要求

- .NET 10（开发用 SDK；运行用 Desktop Runtime）
- Azure Speech Services 订阅（Key + Region）

## 快速开始（开发/本地运行）

```bash
dotnet restore
dotnet run
```

首次运行按界面提示填写 Azure Speech 的订阅信息，并选择输入来源与设备。

## 平台与限制（务必先读）

- WASAPI Loopback 与 WAV→MP3（Media Foundation）主要面向 Windows
- Windows on ARM：依赖层面支持 `win-arm64`，但建议真机验证音频链路（录音/环回/识别）

## 依赖与版本

项目依赖版本以 NuGet 引用为准（见 TrueFluentPro.csproj），以下为当前核心组件与版本：

- .NET：.NET 10（运行时依赖 Desktop Runtime）
- Avalonia UI：Avalonia 11.3.11 / Avalonia.Desktop 11.3.11
- AvaloniaEdit：11.4.1（11.x 最新）
- Markdown 渲染：Markdown.Avalonia 11.0.2
- 语音识别/翻译：Microsoft.CognitiveServices.Speech 1.44.0
- 音频采集/处理：NAudio 2.2.1
- 配置/序列化：Newtonsoft.Json 13.0.3

## 构建期工具：IconGen（不耦合主程序）

为设置可执行文件图标，项目包含独立构建期工具 `tools/IconGen`：

- 构建时从 `Assets/AppIcon.png` 生成多尺寸 `.ico`
- 尽力复制到 `Assets/AppIcon.ico`
- 即使生成失败也 **不阻断** 主程序构建（图标只是附加体验）
- 工具依赖：SixLabors.ImageSharp 3.1.10（仅工具使用，主程序不引用）

## 内容更新方式（关于/帮助）

关于与帮助页面内容来自 Markdown：

- 优先读取可执行文件同目录的 `About.md` / `Help.md`
- 外部文件不存在时回退到应用内置资源（`Assets/` 作为 AvaloniaResource 内嵌）

发布时如果希望用户可直接改文案，可在打包脚本中使用 `-CopyExternalDocs` 将 `Assets/About.md`、`Assets/Help.md` 外置到发布目录。

## 发布建议（适合 GitHub Releases）

推荐默认发布 FDD（framework-dependent）：
