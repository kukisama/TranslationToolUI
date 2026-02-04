# TranslationToolUI

一个使用主要展示Azure Speech能力，“把声音变成可读字幕”的桌面工具。

TranslationToolUI 会采集音频（麦克风 / 系统回环 Loopback），推送到 Azure Speech 做识别与翻译，并实时显示中间结果与最终结果；可选同步录音，结束后自动转成 MP3。

## 功能概览

- **实时识别/翻译**：边播边出字幕（含中间结果/最终结果）。
- **输入源选择**：麦克风 / **WASAPI Loopback**（把系统正在播放的声音当输入）。
- **浮动字幕**：可置顶显示，适合边看视频/参加各类网络会议，边看翻译。
- **会话录音**：翻译同时写 WAV，停止后异步转 MP3（Windows Media Foundation）。
- **历史记录**：按会话积累文本，适合后续整理。
- **配置持久化**：本地 JSON 保存，启动自动加载。

## 主要使用场景（设计目标）

- **会议纪要助手**：用麦克风收音，实时字幕 + 历史记录，方便会后整理。
- **录制/留存在线视频**：用 Loopback 直接录制浏览器/播放器的声音，得到 WAV/MP3 存档。
- **边看边翻译在线视频**：Loopback 作为输入 + 浮动字幕显示，让视频内容“实时可读”。

## 核心依赖（运行时）

版本以当前项目的 NuGet 引用为准（详见 `TranslationToolUI.csproj`）：

- .NET： .NET 8（[官网](https://dotnet.microsoft.com/)）
- Avalonia UI：Avalonia 11.3.0（[官网](https://avaloniaui.net/) | [NuGet](https://www.nuget.org/packages/Avalonia/11.3.0)）
- Markdown 渲染：Markdown.Avalonia 11.0.2（[GitHub](https://github.com/whistyun/Markdown.Avalonia) | [NuGet](https://www.nuget.org/packages/Markdown.Avalonia/11.0.2)）
- 语音识别/翻译：Microsoft.CognitiveServices.Speech 1.44.0（[文档](https://learn.microsoft.com/azure/ai-services/speech-service/) | [NuGet](https://www.nuget.org/packages/Microsoft.CognitiveServices.Speech/1.44.0)）
- 音频采集/处理：NAudio 2.2.1（[GitHub](https://github.com/naudio/NAudio) | [NuGet](https://www.nuget.org/packages/NAudio/2.2.1)）
- 配置/序列化：Newtonsoft.Json 13.0.3（[NuGet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3)）

## 平台说明（Windows 相关）

- **WASAPI Loopback** 与 **Media Foundation（WAV → MP3）** 主要面向 Windows。
- MP3 转码通常由 Windows 的 Media Foundation 编码器能力决定；不同 Windows 版本/精简系统可能存在差异。
- 在不支持的环境中，应用应回退到更保守的输入路径（例如默认麦克风），保证“不崩溃、可运行”。

## 构建期工具（可选，不耦合主程序）
为在 Windows 上设置可执行文件图标，项目包含一个独立的 IconGen构建期工具。
- 依赖：SixLabors.ImageSharp 3.1.10（[官网](https://sixlabors.com/) | [NuGet](https://www.nuget.org/packages/SixLabors.ImageSharp/3.1.10)；**仅工具使用**，主程序不引用该依赖）
- 行为：构建时从 Assets下的AppIcon.png 生成多尺寸 *.ico，并尽力复制到 Assets下的AppIcon.ico。
- 设计目标：即使图标生成失败，主程序也应 **继续构建并可正常运行**。

## 内容更新方式（关于/帮助）
关于与帮助页面内容来自 Markdown：
- **优先读取** 可执行文件同目录的 About.md 以及 Help.md。
- 若外部文件不存在，则回退到应用内置资源
