# 译见 Pro v1.5.0

## 更新
- sora2可以图生视频、同时参考图不合适的情况下，会弹出编辑器辅助修改到合适的图
- 录制支持切换音频源、翻译和录制分开
- 支持微软speech 的中国区的服务终结点
- 解决图生图失败的问题，现在可以挂8张参考图
---

## 下载说明

| 文件 | 说明 |
|------|------|
| `TranslationToolUI-win-x64-fdd.zip` | Windows x64 版本（推荐） |
| `TranslationToolUI-win-arm64-fdd.zip` | Windows ARM64 版本（可选） 实验性： MP3 录音导出功能缺少库，暂时不能做speech。 |

### 运行前提

本版本为 **FDD（Framework-dependent）** 发布，需要预先安装：

- [**.NET 10 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0)

### 使用方式

1. 下载对应架构的 zip 文件
2. 解压到任意目录
3. 运行 `TranslationToolUI.exe`
