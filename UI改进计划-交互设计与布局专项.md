# UI改进计划-交互设计与布局专项

> 本文是 [UI改进计划.md](UI改进计划.md) 的补充文档，专门针对交互设计细节、具体布局方案、组件选型和视觉规范提供完整的设计指南。

---

## 目录

1. [功能区重组详细方案](#1-功能区重组详细方案)
2. [每个视图的详细 Wireframe](#2-每个视图的详细-wireframe)
3. [组件库选型与样式系统](#3-组件库选型与样式系统)
4. [交互流程优化](#4-交互流程优化)
5. [视觉设计规范](#5-视觉设计规范)
6. [Avalonia 可用组件深度分析](#6-avalonia-可用组件深度分析)

---

## 1. 功能区重组详细方案

### 1.1 当前功能区映射

```
当前结构                              建议结构
────────────────                     ────────────────
MainWindow (Live Mode)               侧栏: 实时翻译
  ├── 工具栏                            ├── 紧凑工具栏（设备+语言）
  ├── 设备面板                          ├── 翻译编辑器（全高）
  ├── 翻译编辑器                        ├── 底部历史（可折叠）
  └── 右侧Tab                          └── 操作栏
       ├── 历史记录 Tab              侧栏: AI 洞察（独立页面）
       ├── Insights Tab                  ├── 预设按钮栏
       ├── 文件库 Tab                    ├── 内容输入区
       └── 批量处理 Tab                  └── Markdown 结果展示

MainWindow (Review Mode)             侧栏: 批量处理（独立页面）
  ├── 文件列表                          ├── 拖放上传区
  ├── 播放控件                          ├── DataGrid 任务列表
  ├── 字幕显示                          └── 统计栏
  └── 审查功能
                                     侧栏: 审查表（独立页面）
ConfigCenterView (弹窗)                  ├── 审查模板列表
  └── 9个Tab                            ├── 内容区
                                         └── 结果展示
MediaStudioWindow (弹窗)
  ├── 会话列表                       侧栏: Media Studio（内嵌）
  ├── 聊天内容                          ├── 会话列表
  └── 输入区                            ├── 聊天内容
                                         └── 输入区

                                     侧栏: 文件库（独立页面）
                                         ├── 文件列表（可排序）
                                         └── 播放器 + 字幕

                                     侧栏: 设置（内嵌）
                                         └── Expander 组（9组）

                                     侧栏: 关于/帮助
                                         └── Markdown 内容
```

### 1.2 数据流重组

```
当前: 所有状态在 MainWindowViewModel 中
──────────────────────────────────────────
MainWindowViewModel (4131 LOC, 9 partial files)
  ├── 翻译状态 + 设备状态 + 批量队列 + AI洞察 + 审查
  └── 一个巨大的 DataContext

建议: 每个功能区有独立 ViewModel
──────────────────────────────────────────
MainWindowViewModel (精简为导航+全局状态 ~200 LOC)
  ├── SelectedNavIndex, CurrentView
  ├── GlobalStatusMessage
  ├── IsTranslating (全局标记)
  └── 共享的 AzureSpeechConfig

LiveTranslationViewModel (翻译相关 ~500 LOC)
  ├── 设备选择
  ├── 翻译编辑器状态
  ├── 翻译历史
  └── 字幕导出

BatchProcessingViewModel (批量处理 ~600 LOC)
  ├── 文件队列管理
  ├── 任务并发控制
  └── 批量字幕生成

AiInsightViewModel (AI洞察 ~300 LOC)
  ├── 预设按钮
  ├── 自定义提示
  └── 分析结果

ReviewSheetViewModel (审查表 ~400 LOC)
  ├── 审查模板
  ├── 审查生成
  └── 审查导出

FileLibraryViewModel (文件库 ~200 LOC)
  ├── 文件列表
  ├── 播放控制
  └── 字幕显示

SettingsViewModel (设置 ~300 LOC)
  ├── 订阅管理
  ├── AI配置
  └── 音频设置
```

### 1.3 共享状态管理

```csharp
/// <summary>
/// 全局应用状态，在多个 ViewModel 之间共享
/// </summary>
public class AppState : ObservableObject
{
    [ObservableProperty] private AzureSpeechConfig _config = new();
    [ObservableProperty] private bool _isTranslating;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private AzureSubscription? _activeSubscription;

    // 事件：通知其他 ViewModel 状态变化
    public event EventHandler? TranslationStarted;
    public event EventHandler? TranslationStopped;
    public event EventHandler<TranslationItem>? TranslationReceived;
    public event EventHandler? ConfigChanged;
}

// 在 DI 中注册为 Singleton
services.AddSingleton<AppState>();

// 每个 ViewModel 通过构造函数接收
public LiveTranslationViewModel(
    AppState appState,
    ISpeechTranslationService speechService,
    ...)
{
    _appState = appState;
    // ...
}
```

---

## 2. 每个视图的详细 Wireframe

### 2.1 实时翻译视图 (LiveTranslationView)

```
┌─────────────────────────────────────────────────────────────────┐
│ ┌── 设备与语言栏 (高度:44px) ──────────────────────────────────┐│
│ │ 🎤 [默认麦克风 ▾]   🔊 [系统音频 ▾]   🌐 [中文→英文 ▾]   ⚙││
│ └──────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 显示模式 ──────────────────────────── 操作按钮 ──────────┐ │
│ │ [原文] [译文] [双语]                    [📌字幕] [💾导出]  │ │
│ └──────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 翻译编辑器 (flex: 1, 占满剩余空间) ──────────────────────┐ │
│ │                                                             │ │
│ │  14:30:05                                                   │ │
│ │  今天我们讨论一下项目的最新进展                              │ │
│ │  Today we discuss the latest progress of the project        │ │
│ │  ─────────────────────────────────────────────────────────  │ │
│ │  14:30:18                                                   │ │
│ │  目前进度有一些延迟                                         │ │
│ │  Currently there are some delays in progress                │ │
│ │  ─────────────────────────────────────────────────────────  │ │
│ │  14:30:25                                                   │ │
│ │  ▌ (正在识别...)                                            │ │
│ │                                                             │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                                                 │
│ ┌── 最近翻译 (可折叠, 最大高度: 150px) ──── [展开/折叠 ▾] ──┐ │
│ │ 14:30:25  目前进度有一些延迟 → Currently there are some... │ │
│ │ 14:30:18  今天我们讨论一下... → Today we discuss the...    │ │
│ │ 14:30:05  项目启动以来... → Since the project started...   │ │
│ └──────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 操作栏 (高度:36px) ─────────────────────────────────────┐  │
│ │ [🔴 录音中 00:12:34]     |  AGC: 中  |  🔇 静音检测: 正常│  │
│ └──────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**关键交互：**
- 设备栏固定在顶部，始终可见
- 翻译编辑器自动滚动到最新内容
- 正在识别的文本有脉冲光标动画
- 双击翻译历史项可定位到对应时间
- 右键编辑器可弹出搜索和导出选项

### 2.2 批量处理视图 (BatchProcessingView)

```
┌─────────────────────────────────────────────────────────────────┐
│ 批量转写                              [全部开始] [⏸ 暂停] [清空]│
│                                                                 │
│ ┌── 文件拖放区 (DragDrop=True, 高度:100px) ────────────────────┐│
│ │                                                               ││
│ │     📂  拖放音频文件到这里，或 [点击选择文件]                  ││
│ │     支持格式: WAV, MP3, M4A, FLAC, OGG                       ││
│ │     最大并发: [10 ▾] 个任务                                   ││
│ │                                                               ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── DataGrid 任务列表 (flex: 1) ───────────────────────────────┐│
│ │ ┌──────┬──────────────────┬──────┬────────┬──────┬──────────┐││
│ │ │ 状态 │ 文件名           │ 大小 │ 语言   │ 进度 │ 操作     │││
│ │ ├──────┼──────────────────┼──────┼────────┼──────┼──────────┤││
│ │ │  ✅  │ 会议录音0224.wav │ 45MB │ zh-CN  │ 100% │ [📄][🗑]│││
│ │ │  ⏳  │ interview.mp3    │ 12MB │ en-US  │  67% │ [⏸][🗑]│││
│ │ │  ❌  │ broken_file.wav  │  8MB │ zh-CN  │   0% │ [🔄][🗑]│││
│ │ │  ⏸  │ podcast.m4a      │ 67MB │ ja-JP  │   0% │ [▶][🗑]│││
│ │ │  ⏸  │ lecture.flac     │120MB │ en-US  │   0% │ [▶][🗑]│││
│ │ └──────┴──────────────────┴──────┴────────┴──────┴──────────┘││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 任务统计栏 (高度:32px) ────────────────────────────────────┐│
│ │ 总计: 5 | ✅ 完成: 1 | ⏳ 处理中: 1 | ❌ 失败: 1 | ⏸ 等待: 2│
│ │ 预计剩余: ~15:30                                              ││
│ └───────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

**关键交互：**
- 拖放文件直接添加到队列
- DataGrid 支持列排序（按状态、大小、进度排序）
- 进度条内嵌在 DataGrid 行中
- 失败的任务可以一键重试（🔄）
- 状态图标有颜色编码（绿色=完成，蓝色=处理中，红色=失败，灰色=等待）

### 2.3 AI 洞察视图 (AiInsightView)

```
┌─────────────────────────────────────────────────────────────────┐
│ AI 会议洞察                                          [🤖 配置AI]│
│                                                                 │
│ ┌── 预设分析按钮 (Wrap 布局) ──────────────────────────────────┐│
│ │ [📋 会议摘要] [🧠 知识提取] [😤 投诉检测] [✅ 行动项] [😊 情│
│ │ 感分析] [+ 自定义]                                           ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 内容来源 ─────────────────────────────────────── 选择 ────┐│
│ │ ○ 当前翻译会话内容                                           ││
│ │ ○ 选择已保存的会话文件  [📂 浏览]                             ││
│ │ ○ 自定义输入                                                 ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 自定义问题输入 (可选) ─────────────────────────────────────┐│
│ │ [请针对会议内容提出你的问题或分析需求...]          [➤ 发送]  ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 分析结果 (Markdown 渲染, flex: 1) ─────────────────────────┐│
│ │                                                               ││
│ │  ## 📋 会议摘要                                               ││
│ │                                                               ││
│ │  ### 主要议题                                                 ││
│ │  1. 项目进度延迟 — 需要调整时间表                             ││
│ │  2. 新功能开发 — UI 改版方案已确定                            ││
│ │                                                               ││
│ │  ### 关键决定                                                 ││
│ │  - 将截止日期延后两周                                         ││
│ │  - 增加一名开发人员                                           ││
│ │                                                               ││
│ │  ### 待办事项                                                 ││
│ │  - [ ] 张三: 更新项目计划 (截止 2/28)                        ││
│ │  - [ ] 李四: 完成 UI 原型 (截止 3/3)                         ││
│ │                                                               ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 操作栏 ────────────────────────────────────────────────────┐│
│ │ [📋 复制结果] [💾 保存为 MD] [🔄 重新分析]    自动分析: [开 ]││
│ └───────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

### 2.4 设置视图 (SettingsView)

```
┌─────────────────────────────────────────────────────────────────┐
│ ⚙️ 设置                                            [🔍 搜索]   │
│                                                                 │
│ ┌── ScrollViewer ──────────────────────────────────────────────┐│
│ │                                                               ││
│ │ ▼ 📡 订阅与认证                                               ││
│ │ ┌───────────────────────────────────────────────────────────┐ ││
│ │ │ 订阅列表:                                                 │ ││
│ │ │ ┌──────────────────────────────┬──────────────────────┐   │ ││
│ │ │ │ Azure Global                 │ ✅ 已验证            │   │ ││
│ │ │ │ Azure China                  │ ⚠️ 未验证            │   │ ││
│ │ │ └──────────────────────────────┴──────────────────────┘   │ ││
│ │ │ [+ 添加]  [✏️ 编辑选中]  [🗑 删除选中]  [🧪 测试连接]    │ ││
│ │ │                                                           │ ││
│ │ │ 认证方式:  ○ API 密钥   ○ Azure AD                       │ ││
│ │ │ [AAD 登录面板 - 条件显示]                                 │ ││
│ │ └───────────────────────────────────────────────────────────┘ ││
│ │                                                               ││
│ │ ▶ 🎤 录音与存储                                               ││
│ │   (折叠状态)                                                  ││
│ │                                                               ││
│ │ ▶ 🤖 AI 配置                                                  ││
│ │   (折叠状态)                                                  ││
│ │                                                               ││
│ │ ▶ 🎯 语音识别                                                 ││
│ │   (折叠状态)                                                  ││
│ │                                                               ││
│ │ ▶ 📝 字幕与文本                                               ││
│ │   (折叠状态)                                                  ││
│ │                                                               ││
│ │ ▶ 🔍 诊断                                                     ││
│ │   (折叠状态)                                                  ││
│ │                                                               ││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ [恢复默认值]                           自动保存: ✅ 已启用      │
└─────────────────────────────────────────────────────────────────┘
```

### 2.5 文件库视图 (FileLibraryView)

```
┌─────────────────────────────────────────────────────────────────┐
│ 📁 文件库                      [📂 打开目录] [🔄 刷新] [筛选 ▾]│
│                                                                 │
│ ┌── 文件列表 (上半部) ─────────────────────────────────────────┐│
│ │ ┌──────────────────────┬──────┬──────────┬──────────────────┐││
│ │ │ 文件名               │ 格式 │ 时长     │ 日期             │││
│ │ ├──────────────────────┼──────┼──────────┼──────────────────┤││
│ │ │ 📄 会议0224.mp3      │ MP3  │ 01:23:45 │ 2026-02-24      │││
│ │ │ 📄 会议0224.srt      │ SRT  │ —        │ 2026-02-24      │││
│ │ │ 📄 interview.wav     │ WAV  │ 00:45:12 │ 2026-02-23      │││
│ │ └──────────────────────┴──────┴──────────┴──────────────────┘││
│ └───────────────────────────────────────────────────────────────┘│
│                                                                 │
│ ┌── 播放器 (下半部, GridSplitter 可调) ────────────────────────┐│
│ │                                                               ││
│ │  正在播放: 会议0224.mp3                                       ││
│ │  ──●───────────────────────────── 00:05:32 / 01:23:45         ││
│ │  [⏮] [▶] [⏭]  [🔊 ▬▬▬▬]                                     ││
│ │                                                               ││
│ │  ┌── 字幕面板（与音频同步显示）────────────────────────────┐  ││
│ │  │ 00:05:30  今天的议题是关于下一步的计划                  │  ││
│ │  │ 00:05:32  ► Today's topic is about the next steps       │  ││
│ │  │ 00:05:35  我们需要讨论三个方面                          │  ││
│ │  └──────────────────────────────────────────────────────────┘  ││
│ └───────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. 组件库选型与样式系统

### 3.1 推荐的额外 Avalonia 组件包

| 包名 | 用途 | 安装命令 |
|------|------|----------|
| `Avalonia.Controls.DataGrid` | 批量任务表格 | 已内建在 Avalonia 11 |
| `FluentAvalonia` | Fluent Design 2 控件 | `dotnet add package FluentAvalonia.UI` |
| `Material.Avalonia` | Material Design 组件 | `dotnet add package Material.Avalonia` |
| `Avalonia.Controls.TreeDataGrid` | 高性能树形表格 | `dotnet add package Avalonia.Controls.TreeDataGrid` |

**推荐优先使用 `FluentAvalonia`**，因为：
- 与当前 FluentTheme 风格一致
- 提供 NavigationView、InfoBar、ContentDialog 等缺失控件
- 微软 WinUI 3 的 Avalonia 移植，UI 一致性好

### 3.2 FluentAvalonia 关键组件

```xml
<!-- 需要先安装: dotnet add package FluentAvalonia.UI -->
<!-- 在 App.axaml 中注册 -->
<Application xmlns:ui="using:FluentAvalonia.UI.Controls">
  <Application.Styles>
    <FluentTheme/>
    <!-- FluentAvalonia 主题已内建支持 -->
  </Application.Styles>
</Application>
```

**NavigationView（替代手写侧边栏）：**

```xml
<ui:NavigationView IsSettingsVisible="True"
                   PaneDisplayMode="Left"
                   IsBackButtonVisible="False"
                   SelectedItem="{Binding SelectedNavItem}">

  <ui:NavigationView.MenuItems>
    <ui:NavigationViewItem Content="实时翻译" Tag="live">
      <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="Microphone"/>
      </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>

    <ui:NavigationViewItem Content="批量处理" Tag="batch">
      <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="List"/>
      </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>

    <ui:NavigationViewItem Content="AI 洞察" Tag="insight">
      <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="Lightbulb"/>
      </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>

    <ui:NavigationViewItem Content="Media Studio" Tag="media">
      <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="Video"/>
      </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>

    <ui:NavigationViewItemSeparator/>

    <ui:NavigationViewItem Content="文件库" Tag="files">
      <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="Folder"/>
      </ui:NavigationViewItem.Icon>
    </ui:NavigationViewItem>
  </ui:NavigationView.MenuItems>

  <!-- 内容区 -->
  <ui:Frame>
    <ContentControl Content="{Binding CurrentView}">
      <ContentControl.ContentTransition>
        <CrossFade Duration="0:0:0.15"/>
      </ContentControl.ContentTransition>
    </ContentControl>
  </ui:Frame>
</ui:NavigationView>
```

**InfoBar（替代 StatusMessage）：**

```xml
<ui:InfoBar Title="{Binding NotificationTitle}"
            Message="{Binding NotificationMessage}"
            Severity="{Binding NotificationSeverity}"
            IsOpen="{Binding IsNotificationVisible}"
            IsClosable="True"/>

<!-- Severity 枚举: Informational, Success, Warning, Error -->
```

**ContentDialog（替代弹窗）：**

```csharp
// 使用 ContentDialog 替代 Window.ShowDialog
var dialog = new ContentDialog
{
    Title = "确认删除",
    Content = "确定要删除这个会话吗？此操作不可撤销。",
    PrimaryButtonText = "删除",
    CloseButtonText = "取消",
    DefaultButton = ContentDialogButton.Close
};

var result = await dialog.ShowAsync();
if (result == ContentDialogResult.Primary)
{
    // 执行删除
}
```

**NumberBox（替代 NumericUpDown）：**

```xml
<!-- 更现代的数字输入 -->
<ui:NumberBox Header="批量并发数"
              Value="{Binding BatchConcurrencyLimit}"
              Minimum="1" Maximum="20"
              SpinButtonPlacementMode="Inline"/>
```

### 3.3 全局样式系统设计

```xml
<!-- Styles/GlobalStyles.axaml -->
<Styles xmlns="https://github.com/avaloniaui">

  <!-- ═════ 按钮样式 ═════ -->

  <!-- 主要按钮 -->
  <Style Selector="Button.primary">
    <Setter Property="Background" Value="{DynamicResource PrimaryBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="CornerRadius" Value="6"/>
    <Setter Property="Padding" Value="16,8"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
  </Style>
  <Style Selector="Button.primary:pointerover">
    <Setter Property="Background" Value="{DynamicResource PrimaryHoverBrush}"/>
    <Setter Property="Cursor" Value="Hand"/>
  </Style>

  <!-- 次要按钮 -->
  <Style Selector="Button.secondary">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="6"/>
    <Setter Property="Padding" Value="12,6"/>
  </Style>

  <!-- 幽灵按钮 (无边框) -->
  <Style Selector="Button.ghost">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="Opacity" Value="0.7"/>
  </Style>
  <Style Selector="Button.ghost:pointerover">
    <Setter Property="Opacity" Value="1"/>
    <Setter Property="Background" Value="#10000000"/>
  </Style>

  <!-- 紧凑按钮 -->
  <Style Selector="Button.compact">
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="MinHeight" Value="26"/>
    <Setter Property="CornerRadius" Value="4"/>
  </Style>

  <!-- 危险按钮 -->
  <Style Selector="Button.danger">
    <Setter Property="Background" Value="{DynamicResource ErrorBrush}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="CornerRadius" Value="6"/>
  </Style>

  <!-- ═════ 卡片样式 ═════ -->

  <Style Selector="Border.card">
    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}"/>
    <Setter Property="CornerRadius" Value="8"/>
    <Setter Property="Padding" Value="16"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}"/>
    <Setter Property="BorderThickness" Value="1"/>
  </Style>

  <!-- ═════ 标题样式 ═════ -->

  <Style Selector="TextBlock.page-title">
    <Setter Property="FontSize" Value="24"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="Margin" Value="0,0,0,16"/>
  </Style>

  <Style Selector="TextBlock.section-title">
    <Setter Property="FontSize" Value="16"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Margin" Value="0,16,0,8"/>
  </Style>

  <Style Selector="TextBlock.label">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
  </Style>

  <Style Selector="TextBlock.caption">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}"/>
  </Style>

  <!-- ═════ 输入框样式 ═════ -->

  <Style Selector="TextBox.search-box">
    <Setter Property="CornerRadius" Value="8"/>
    <Setter Property="Padding" Value="12,8"/>
    <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}"/>
  </Style>

  <!-- ═════ 分段控件样式 ═════ -->

  <Style Selector="ToggleButton.segment">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12,4"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Cursor" Value="Hand"/>
  </Style>
  <Style Selector="ToggleButton.segment:checked">
    <Setter Property="Background" Value="White"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="BoxShadow" Value="0 1 3 0 #20000000"/>
  </Style>

</Styles>
```

---

## 4. 交互流程优化

### 4.1 首次启动流程

```
用户首次打开应用
    │
    ├─ 检测到无配置文件
    │
    ▼
┌─ Onboarding 向导 (ContentDialog 形式) ─┐
│                                         │
│  第 1 步: 欢迎                          │
│  "欢迎使用译见 Pro！"                   │
│  [下一步]                               │
│                                         │
│  第 2 步: 添加 Azure 订阅               │
│  密钥: [____________]                   │
│  区域: [eastasia ▾]                     │
│  [测试连接] [跳过] [下一步]             │
│                                         │
│  第 3 步: 选择语言                       │
│  源语言: [中文 ▾]                       │
│  目标语言: [英文 ▾]                     │
│  [完成]                                 │
└─────────────────────────────────────────┘
    │
    ▼
正常使用（实时翻译页面）
```

### 4.2 实时翻译操作流程

```
用户点击"开始翻译" (F5)
    │
    ├─ 检查订阅是否有效
    │   ├─ 无效 → InfoBar: "请先配置 Azure 订阅" (Warning)
    │   └─ 有效 → 继续
    │
    ├─ 检查音频设备
    │   ├─ 无设备 → InfoBar: "未检测到麦克风" (Error)
    │   └─ 有设备 → 继续
    │
    ├─ 启动翻译
    │   ├─ 工具栏: "开始翻译" 按钮变为 "停止翻译"
    │   ├─ 状态栏: 显示 🔴 录音指示器 + 计时器
    │   ├─ 音频可视化: 开始显示电平
    │   └─ 编辑器: 开始接收翻译结果
    │
    ├─ 翻译进行中
    │   ├─ 中间结果: 灰色文字 + 闪烁光标
    │   ├─ 最终结果: 正常文字 + 添加到历史
    │   └─ 错误: InfoBar 显示错误 (非阻断)
    │
    └─ 用户点击"停止翻译" (F6)
        ├─ 停止录音
        ├─ 异步转码 (WAV→MP3)
        ├─ InfoBar: "翻译已停止，录音文件已保存" (Success)
        └─ 恢复初始状态
```

### 4.3 设置修改流程

```
用户通过侧栏进入设置页面
    │
    ├─ 展开目标设置组 (Expander)
    │
    ├─ 修改设置值
    │   ├─ 每次修改触发防抖自动保存 (500ms)
    │   └─ 状态栏: "设置已自动保存 ✓"
    │
    ├─ 特殊设置 (需要验证)
    │   ├─ 修改 API 密钥 → [测试连接] 按钮高亮
    │   ├─ 测试通过 → ✅ 标记 + InfoBar "连接成功"
    │   └─ 测试失败 → ❌ 标记 + InfoBar "连接失败: {原因}"
    │
    └─ 离开设置页面
        └─ 自动保存已在修改时完成，无需额外操作
```

---

## 5. 视觉设计规范

### 5.1 色彩系统

```
语义化颜色体系:

┌─ 主色调 ────────────────────────────────────────────┐
│ Primary:        #2563EB (蓝色 - 主操作)             │
│ PrimaryHover:   #1D4ED8 (深蓝 - 悬停)              │
│ PrimaryLight:   #EFF6FF (浅蓝 - 选中背景)          │
└─────────────────────────────────────────────────────┘

┌─ 功能色 ────────────────────────────────────────────┐
│ Success:        #16A34A (绿色 - 成功/完成)          │
│ SuccessLight:   #E8F5E9 (浅绿 - 成功背景)          │
│ Warning:        #F59E0B (橙色 - 警告)               │
│ WarningLight:   #FFF3E0 (浅橙 - 警告背景)          │
│ Error:          #DC2626 (红色 - 错误/危险)          │
│ ErrorLight:     #FFEBEE (浅红 - 错误背景)           │
│ Info:           #2563EB (蓝色 - 信息)               │
│ InfoLight:      #E3F2FD (浅蓝 - 信息背景)          │
└─────────────────────────────────────────────────────┘

┌─ 中性色 ────────────────────────────────────────────┐
│ Background:     #FFFFFF (白色 - 页面背景)           │
│ Surface:        #F8FAFC (极浅灰 - 面板/卡片)       │
│ SurfaceAlt:     #F1F5F9 (浅灰 - 状态栏/边栏)      │
│ Border:         #E5E7EB (边框)                      │
│ BorderSubtle:   #CBD5E1 (微弱边框)                  │
│ Divider:        #E2E8F0 (分割线)                    │
│                                                     │
│ TextPrimary:    #0F172A (深色 - 主要文字)           │
│ TextSecondary:  #475569 (中灰 - 次要文字)          │
│ TextMuted:      #94A3B8 (浅灰 - 提示文字)          │
│ TextDisabled:   #CBD5E1 (禁用文字)                  │
└─────────────────────────────────────────────────────┘
```

### 5.2 字体规范

```
字体家族: Segoe UI Variable, Segoe UI, Microsoft YaHei UI, sans-serif

层级:
  页面标题:     24px, Bold (FontWeight=700)
  区域标题:     18px, SemiBold (FontWeight=600)
  小节标题:     16px, SemiBold (FontWeight=600)
  正文:         14px, Regular (FontWeight=400)        ← 默认
  标签/描述:    13px, Regular
  注释/时间戳:  12px, Regular, TextSecondary
  徽章/小字:    11px, Medium (FontWeight=500)

等宽字体 (代码/密钥):
  Cascadia Code, Consolas, monospace
  大小: 13px
```

### 5.3 间距系统

```
基础单位: 4px

间距级别:
  xs:   4px   (紧凑元素间)
  sm:   8px   (同组元素间)
  md:   12px  (不同元素间)
  lg:   16px  (区域内边距)
  xl:   24px  (区域间距)
  2xl:  32px  (大区块间距)

圆角:
  sm:   4px   (按钮、输入框)
  md:   6px   (卡片)
  lg:   8px   (面板)
  xl:   12px  (对话框)
  full: 999px (药丸形按钮)
```

### 5.4 阴影系统

```
shadow-sm:   0 1px 2px rgba(0,0,0,0.05)     ← 卡片默认
shadow-md:   0 4px 6px rgba(0,0,0,0.07)     ← 悬停/弹出
shadow-lg:   0 10px 15px rgba(0,0,0,0.10)   ← 对话框/浮动面板
shadow-xl:   0 20px 25px rgba(0,0,0,0.15)   ← 模态窗口
```

---

## 6. Avalonia 可用组件深度分析

### 6.1 原生 Avalonia 11 组件利用

| 组件 | 当前使用 | 建议场景 | 优先级 |
|------|----------|----------|--------|
| **SplitView** | ❌ 未使用 | 主窗口侧边栏导航 | 🔴 高 |
| **DataGrid** | ❌ 未使用 | 批量任务列表、文件库 | 🔴 高 |
| **GridSplitter** | ❌ 未使用 | 编辑器/历史分割、文件库/播放器分割 | 🟡 中 |
| **Expander** | ✅ 已使用 | 更多场景: 设置组、高级选项 | 🟡 扩展 |
| **Flyout** | ❌ 未使用 | 高级音频设置、快捷操作 | 🟡 中 |
| **AutoCompleteBox** | ❌ 未使用 | 设置搜索、命令搜索 | 🟢 低 |
| **CalendarDatePicker** | ❌ 未使用 | 按日期筛选会话/文件 | 🟢 低 |
| **TreeView** | ❌ 未使用 | 文件目录浏览 | 🟢 低 |
| **CrossFade** | ❌ 未使用 | 页面切换过渡 | 🟡 中 |
| **PageSlide** | ❌ 未使用 | 设置组切换动画 | 🟢 低 |

### 6.2 FluentAvalonia 组件利用

| 组件 | 替代对象 | 效果 | 优先级 |
|------|----------|------|--------|
| **NavigationView** | 手写 SplitView + ListBox | 内建面包屑、折叠、动画 | 🔴 高 |
| **InfoBar** | StatusMessage 文字 | 彩色通知条 + 自动消失 | 🔴 高 |
| **ContentDialog** | Window.ShowDialog | 原生模态对话框 | 🟡 中 |
| **NumberBox** | NumericUpDown | 更现代的数字输入 | 🟡 中 |
| **ProgressRing** | ProgressBar (不确定) | 圆形加载指示 | 🟡 中 |
| **TeachingTip** | ToolTip | 引导提示气泡 | 🟢 低 |
| **CommandBar** | StackPanel 按钮 | 工具栏 + 溢出菜单 | 🟢 低 |
| **SegmentedControl** | RadioButton / ToggleButton | 分段选择器 | 🟡 中 |

### 6.3 安装和使用 FluentAvalonia

```bash
# 安装
dotnet add package FluentAvalonia.UI

# App.axaml 中引入
# FluentAvalonia 已自动注入样式，无需额外配置
```

```xml
<!-- 在 AXAML 中使用 -->
<Window xmlns:ui="using:FluentAvalonia.UI.Controls"
        xmlns:uip="using:FluentAvalonia.UI.Controls.Primitives">

  <!-- NavigationView 示例 -->
  <ui:NavigationView>
    <ui:NavigationView.MenuItems>
      <ui:NavigationViewItem Content="Home" Icon="Home"/>
    </ui:NavigationView.MenuItems>
    <ContentControl Content="{Binding CurrentView}"/>
  </ui:NavigationView>

</Window>
```

---

## 实施检查清单

### Phase 0: 准备工作（第 0 周）

- [ ] 安装 `FluentAvalonia.UI` NuGet 包
- [ ] 安装 `Avalonia.Controls.DataGrid` 确认可用
- [ ] 创建 `Styles/` 目录和 `GlobalStyles.axaml`
- [ ] 定义语义化颜色资源（浅色主题）
- [ ] 在 App.axaml 中引入全局样式

### Phase 1: 导航重构（第 1-2 周）

- [ ] 将主内容区拆分为独立 UserControl 视图:
  - [ ] LiveTranslationView.axaml
  - [ ] BatchProcessingView.axaml
  - [ ] AiInsightView.axaml
  - [ ] FileLibraryView.axaml
  - [ ] SettingsView.axaml
- [ ] 在 MainWindow.axaml 中实现 NavigationView 侧边栏
- [ ] 实现导航切换 + ContentTransition 过渡
- [ ] 验证所有功能正常工作

### Phase 2: 核心视图优化（第 2-4 周）

- [ ] LiveTranslationView: 紧凑设备栏 + SegmentedControl + 可折叠历史
- [ ] BatchProcessingView: DataGrid + 拖放 + 进度条
- [ ] AiInsightView: 预设按钮栏 + Markdown 结果
- [ ] SettingsView: Expander 组 + 自动保存 + 搜索

### Phase 3: 状态反馈升级（第 4-5 周）

- [ ] 引入 InfoBar 通知系统
- [ ] 添加翻译状态动画（脉冲指示器）
- [ ] 添加全局快捷键
- [ ] 添加 AutomationProperties 无障碍属性

### Phase 4: 视觉精细化（第 5-6 周）

- [ ] 替换所有硬编码颜色为语义化资源
- [ ] 添加深色主题颜色定义
- [ ] 微交互动效（按钮反馈、悬停效果）
- [ ] 响应式侧边栏折叠
- [ ] 整体视觉一致性审查
