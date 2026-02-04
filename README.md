# TranslationToolUI

跨平台实时语音翻译工具，基于 Avalonia UI 和 Azure Speech Services 构建。

## 功能特性

- 🎤 **实时语音识别以及翻译**：支持多种语言的语音输入,检测麦克风后，从麦克风获取实时音频
- 💬 **智能字幕**：实时显示带翻译的字幕
- 📝 **历史记录**：自动保存翻译历史，支持搜索和管理
- ⚙️ **灵活配置**：可配置 Azure 服务密钥、语言设置等
- 🖥️ **跨平台**：支持 Windows、macOS、Linux
- 🎨 **现代UI**：基于 Avalonia UI 的现代化界面

## 系统要求

- .NET 8.0 或更高版本
- Azure Speech Services 订阅
- Azure Translator 订阅（可选）

## 快速开始

### 1. 克隆项目
```bash
git clone https://github.com/你的用户名/TranslationToolUI.git
cd TranslationToolUI
```

### 2. 安装依赖
```bash
dotnet restore
```

### 3. 配置 Azure 服务
在首次运行时，程序会提示配置 Azure Speech Services：
- 订阅密钥 (Subscription Key)
- 服务区域 (Service Region)
- 识别语言和翻译目标语言

### 4. 运行程序
```bash
dotnet run
```

## 项目架构

### 核心组件

- **PathManager**: 跨平台路径管理，统一管理配置、会话、日志等路径
- **ConfigurationService**: 配置管理，支持Azure服务配置
- **SpeechTranslationService**: 语音识别和翻译核心服务
- **SessionService**: 会话和历史记录管理
- **MainWindowViewModel**: 主界面MVVM逻辑

### 目录结构

```
TranslationToolUI/
├── Models/              # 数据模型
├── Services/            # 业务服务
├── ViewModels/          # MVVM 视图模型
├── Views/               # UI 界面
├── Controls/            # 自定义控件
├── Converters/          # 数据转换器
└── Assets/              # 静态资源
```

## 核心特性详解

### 跨平台路径管理
使用 `PathManager` 实现跨平台路径统一管理：
- Windows: `%APPDATA%/TranslationToolUI/`
- macOS: `~/Library/Application Support/TranslationToolUI/`
- Linux: `~/.config/TranslationToolUI/`

### 智能配置系统
- 自动检测和创建配置目录
- 支持配置文件热加载
- 敏感信息安全存储

### 历史记录管理
- 自动保存翻译历史
- 支持按日期、语言筛选
- 快速搜索和导出功能

## 开发说明

### 环境设置
```bash
# 安装 .NET 8 SDK
# 克隆项目
git clone <repository-url>
cd TranslationToolUI

# 还原包
dotnet restore

# 运行
dotnet run


```

## 说明
虽然代码按照跨平台规范设计，但并没有在Windows以外的真实环境进行过测试。请在实际部署前进行充分测试。
 
## 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 致谢

- [Avalonia UI](https://avaloniaui.net/) - 跨平台 .NET UI 框架
- [Azure Speech Services](https://azure.microsoft.com/services/cognitive-services/speech-services/) - 语音识别和翻译服务
- 所有贡献者和测试用户

 

---

⭐ 如果这个项目对您有帮助，请给个 Star！