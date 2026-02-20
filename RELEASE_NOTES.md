# 译见 Pro v1.6.0

## 更新
- 架构更新
- 因为微软aoai终结点的变更，已经有类似于Openai兼容模式的使用方式，导致使用更简单了，因此逻辑也跟着变更
- 日志系统更新
- 帮助菜单增加版本号显示（从 RELEASE_NOTES.md 第一行自动读取）
- 修复bug
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
