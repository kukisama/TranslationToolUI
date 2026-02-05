# TranslationToolUI

![TranslationToolUI Logo](Assets/AppIcon.png)

使用Azure Speech服务，把声音变成可读字幕：采集音频（麦克风/系统回环），推送到 Azure Speech 做识别与翻译，实时显示中间结果与最终结果；并提供浮动字幕、会话历史与可选录音。

> 说明：项目基于 Avalonia（理论可跨平台），但目前主要在 Windows 环境验证与使用；其它平台请先充分测试。

## 你可能会怎么用它（偏“使用场景”）

- 看外语视频/课程：用 **Loopback** 把系统正在播放的声音当输入，字幕实时可读
- 线上会议/直播：开着浮动字幕，边听边看（也能留会话历史）
- 做笔记/留存：同场会话同步录音（WAV），停止后自动转 MP3（Windows）

## 功能概览

- 实时识别/翻译：支持中间结果/最终结果字幕
- 输入源选择：默认麦克风 / WASAPI 设备选择 / WASAPI Loopback
- 浮动字幕：可置顶显示
- 会话录音：翻译同时写 WAV，停止后异步转 MP3（Windows Media Foundation）
- 历史记录：按会话保存，方便后续整理
- 配置持久化：本地 JSON 保存，启动自动加载

## 运行要求

- .NET 8（开发用 SDK；运行用 Desktop Runtime）
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

## 内容更新（关于/帮助）

应用内“关于/帮助”内容来自 Markdown：

- 优先读取可执行文件同目录的 `About.md` / `Help.md`
- 外部文件不存在时回退到应用内置资源

如果你希望发布后可直接改文案（不重编译），可以在打包脚本里加 `-CopyExternalDocs`。

## 更多说明

- 详细说明与依赖/设计取舍：见 [PROJECT_DETAILS.md](PROJECT_DETAILS.md)

## 许可证

MIT，见 [LICENSE](LICENSE)

## 致谢

- [Avalonia UI](https://avaloniaui.net/)
- [Azure Speech Services](https://learn.microsoft.com/azure/ai-services/speech-service/)
