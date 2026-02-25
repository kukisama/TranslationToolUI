# UI改进计划

> 基于对 TrueFluentPro 当前 Avalonia UI 界面的全面审查，提出以下 UI/UX 改进方案。所有建议均基于 Avalonia 11.x 可用组件、Fluent Design 原则和人体工学最佳实践，附带具体的布局方案和实施细节。

---

## 目录

1. [整体布局重新设计](#1-整体布局重新设计)
2. [导航系统升级](#2-导航系统升级)
3. [配置中心重设计](#3-配置中心重设计)
4. [实时翻译面板优化](#4-实时翻译面板优化)
5. [批量处理与审查面板优化](#5-批量处理与审查面板优化)
6. [Media Studio 改进](#6-media-studio-改进)
7. [状态反馈系统升级](#7-状态反馈系统升级)
8. [键盘快捷键与无障碍](#8-键盘快捷键与无障碍)
9. [深色模式与主题系统](#9-深色模式与主题系统)
10. [响应式布局](#10-响应式布局)
11. [微交互与动效](#11-微交互与动效)
12. [新增功能组件建议](#12-新增功能组件建议)

> 📎 详细的交互设计与布局专项方案见 [UI改进计划-交互设计与布局专项.md](UI改进计划-交互设计与布局专项.md)

---

## 1. 整体布局重新设计

### 1.1 当前布局分析

```
当前: 4行Grid布局
┌──────────────────────────────────────────────────────┐
│ Row 0: 工具栏 (Logo + 按钮 + 音频可视化 + 模式切换)    │
│ Row 1: 设备选择面板 (订阅/麦克风/回环/语言/翻译按钮)    │  ← 仅Live模式显示
│ Row 2: 主内容区 (左:翻译编辑器 | 右:Tab控件4个标签)     │
│ Row 3: 底部状态栏 / 播放控件                           │
└──────────────────────────────────────────────────────┘
```

**问题识别：**
- 顶部两行占据大量垂直空间（约 120px），挤压核心内容区
- 设备选择面板在 Live 模式始终显示，常用操作实际只有「开始/停止翻译」
- 右侧 4 个 Tab 在小屏幕上标签过多，内容区域不足
- Live 模式和 Review 模式通过 RadioButton 切换，发现性差

### 1.2 新布局方案

```
方案: SplitView + 折叠面板

┌─────────────────────────────────────────────────────────────┐
│ ┌───────┐                                                   │
│ │       │  🎤 TrueFluentPro     [▶ 开始] [⏹ 停止]  [⚙]   │
│ │ 导    │                                                   │
│ │ 航    │ ┌─────────────────────────────────────────────┐   │
│ │ 侧    │ │                                             │   │
│ │ 栏    │ │          核心内容区                          │   │
│ │       │ │    (翻译编辑器 / 批量处理 / AI 洞察)         │   │
│ │ 🎤    │ │                                             │   │
│ │ 📋    │ │                                             │   │
│ │ 🤖    │ │                                             │   │
│ │ 🎬    │ │                                             │   │
│ │ ⚙️    │ │                                             │   │
│ │       │ └─────────────────────────────────────────────┘   │
│ │       │ ┌─────────────────────────────────────────────┐   │
│ │       │ │ 状态栏: 就绪 | 🟢 连接正常 | 00:12:34      │   │
│ └───────┘ └─────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 1.3 核心改进点

| 改进项 | 当前 | 改进后 | 收益 |
|--------|------|--------|------|
| 导航方式 | RadioButton 模式切换 | 侧边栏 NavigationView | 导航可扩展、更直观 |
| 设备选择 | 固定面板占空间 | 折叠式/弹出式设置 | 节省约 50px 垂直空间 |
| 工具栏 | 平铺按钮 | CommandBar 式工具栏 | 溢出处理、一致性 |
| 内容区 | 固定左右分栏 | 可调节 SplitView | 用户自定义布局 |
| 状态栏 | 简单文字 | InfoBar + 进度指示 | 更丰富的反馈 |

### 1.4 AXAML 布局骨架

```xml
<Window xmlns="https://github.com/avaloniaui"
        MinWidth="1100" MinHeight="600">

  <DockPanel>
    <!-- 顶部工具栏 -->
    <Border DockPanel.Dock="Top" Height="48" Background="#FFF8FAFC">
      <Grid ColumnDefinitions="Auto,*,Auto">
        <!-- 左: 品牌 -->
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="12"
                    VerticalAlignment="Center" Margin="16,0">
          <Image Source="/Assets/AppIcon.png" Width="28" Height="28"/>
          <TextBlock Text="译见 Pro" FontSize="16" FontWeight="SemiBold"/>
        </StackPanel>

        <!-- 中: 核心操作 -->
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8"
                    HorizontalAlignment="Center" VerticalAlignment="Center">
          <Button Classes="primary" Content="▶ 开始翻译"
                  Command="{Binding StartTranslationCommand}"/>
          <Button Content="⏹ 停止"
                  Command="{Binding StopTranslationCommand}"/>
          <!-- 音频电平指示（紧凑版） -->
          <Border Width="80" Height="24" CornerRadius="4"
                  Background="#FFEEEEEE">
            <ItemsControl Items="{Binding AudioLevels}"/>
          </Border>
        </StackPanel>

        <!-- 右: 工具按钮 -->
        <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4"
                    Margin="0,0,16,0" VerticalAlignment="Center">
          <Button ToolTip.Tip="配置" Content="⚙"/>
          <Button ToolTip.Tip="帮助" Content="❓"/>
        </StackPanel>
      </Grid>
    </Border>

    <!-- 底部状态栏 -->
    <Border DockPanel.Dock="Bottom" Height="28" Background="#FFF1F5F9">
      <Grid ColumnDefinitions="*,Auto,Auto,Auto">
        <TextBlock Grid.Column="0" Text="{Binding StatusMessage}"
                   Margin="16,0" VerticalAlignment="Center" FontSize="12"/>
        <Ellipse Grid.Column="1" Width="8" Height="8"
                 Fill="{Binding SubscriptionLampColor}" Margin="8,0"/>
        <TextBlock Grid.Column="2" Text="{Binding SessionDuration}"
                   FontSize="12" Margin="8,0"/>
      </Grid>
    </Border>

    <!-- 主体: 侧边栏 + 内容 -->
    <SplitView IsPaneOpen="True" DisplayMode="CompactInline"
               OpenPaneLength="200" CompactPaneLength="48"
               PaneBackground="#FFF8FAFC">
      <SplitView.Pane>
        <ListBox SelectedIndex="{Binding SelectedNavIndex}"
                 Classes="nav-list">
          <ListBoxItem>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <TextBlock Text="🎤" FontSize="18"/>
              <TextBlock Text="实时翻译"/>
            </StackPanel>
          </ListBoxItem>
          <ListBoxItem>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <TextBlock Text="📋" FontSize="18"/>
              <TextBlock Text="批量处理"/>
            </StackPanel>
          </ListBoxItem>
          <ListBoxItem>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <TextBlock Text="🤖" FontSize="18"/>
              <TextBlock Text="AI 洞察"/>
            </StackPanel>
          </ListBoxItem>
          <ListBoxItem>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <TextBlock Text="🎬" FontSize="18"/>
              <TextBlock Text="Media Studio"/>
            </StackPanel>
          </ListBoxItem>
          <ListBoxItem>
            <StackPanel Orientation="Horizontal" Spacing="12">
              <TextBlock Text="⚙️" FontSize="18"/>
              <TextBlock Text="设置"/>
            </StackPanel>
          </ListBoxItem>
        </ListBox>
      </SplitView.Pane>

      <!-- 内容区 -->
      <ContentControl Content="{Binding CurrentView}"/>
    </SplitView>
  </DockPanel>
</Window>
```

---

## 2. 导航系统升级

### 2.1 从 RadioButton 切换到功能导航

**当前问题：**
- 用 RadioButton 切换 Live/Review 模式只有两个选项，不利于功能扩展
- 用户需要先理解「模式」的概念才能找到功能

**改进方案：按功能直接导航**

```
侧边栏导航项:
┌────────────────┐
│ 🎤 实时翻译    │ → 实时语音翻译功能（原 Live Conference）
│ 📋 批量处理    │ → 批量音频转写（原 Batch/Review Mode 的队列部分）
│ 🤖 AI 洞察     │ → AI 会议分析（原右侧 Tab 的 Insights）
│ 📝 审查表      │ → 审查总结生成（原 Review Sheet 功能）
│ 🎬 媒体工作室  │ → AI 图片/视频生成（原独立窗口）
│ ────────────── │
│ 📁 文件库      │ → 音频文件管理
│ ⚙️ 设置        │ → 配置中心（原独立窗口）
│ ❓ 帮助        │ → 帮助文档
└────────────────┘
```

### 2.2 导航架构（ViewModel 层）

```csharp
// MainWindowViewModel.cs - 导航支持
public partial class MainWindowViewModel
{
    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private object? _currentView;

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _liveTranslationView,     // 实时翻译
            1 => _batchProcessingView,     // 批量处理
            2 => _aiInsightView,           // AI 洞察
            3 => _reviewSheetView,         // 审查表
            4 => _mediaStudioView,         // 媒体工作室
            5 => _fileLibraryView,         // 文件库
            6 => _settingsView,            // 设置
            7 => _helpView,                // 帮助
            _ => _liveTranslationView
        };
    }
}
```

### 2.3 将嵌入式功能提取为独立视图

当前 MainWindow.axaml 的 664 行代码需要拆分为独立的 UserControl：

```
Views/
├── LiveTranslationView.axaml      # 🆕 实时翻译（原 MainWindow 的 Live 模式部分）
├── BatchProcessingView.axaml      # 🆕 批量处理（原 MainWindow 的 Review 模式部分）
├── AiInsightView.axaml            # 🆕 AI 洞察（原右侧 Insights Tab）
├── ReviewSheetView.axaml          # 🆕 审查表（原审查功能部分）
├── FileLibraryView.axaml          # 🆕 文件库（原右侧 File Library Tab）
├── SettingsView.axaml             # 🆕 内嵌设置（替代弹窗式 ConfigCenter）
├── MediaStudioView.axaml          # 重构: 从独立窗口改为内嵌视图
├── AboutView.axaml                # 保持
├── HelpView.axaml                 # 保持
└── ...
```

**核心收益：**
- MainWindow.axaml 从 664 行减少到 ~100 行（只有骨架布局）
- 每个功能视图独立维护、独立测试
- 功能模块可以更自由地组合和复用

---

## 3. 配置中心重设计

### 3.1 当前问题

- ConfigCenterView 是 980×720 的独立窗口，打开后遮挡主窗口
- 9 个左侧 Tab 标签名过长，在小窗口下显示不全
- 部分设置（如订阅、语言）在主窗口和 ConfigCenter 中重复出现
- 配置修改后需要手动保存

### 3.2 改进方案：内嵌式设置

**将设置集成到侧边栏导航中，而不是独立窗口：**

```
设置页面结构:
┌──────────────────────────────────────────────────────────────┐
│ ⚙️ 设置                                          [🔍 搜索]  │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│ 📡 订阅与认证                                      [展开 ▼]  │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ 当前订阅: [Azure Global ▾]  [🧪 测试连接]              │ │
│ │ 密钥: [••••••••]  [👁]                                  │ │
│ │ 区域: [eastasia ▾]                                      │ │
│ │ [+ 添加订阅]  [✏️ 编辑]  [🗑️ 删除]                     │ │
│ └──────────────────────────────────────────────────────────┘ │
│                                                              │
│ 🎤 录音与存储                                      [展开 ▼]  │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ MP3 比特率: [128 kbps ▾]                                │ │
│ │ 会话目录: [C:\Users\...\Sessions]  [📂 浏览]           │ │
│ └──────────────────────────────────────────────────────────┘ │
│                                                              │
│ 🤖 AI 配置                                        [展开 ▼]  │
│ │ (折叠状态)                                                │
│                                                              │
│ 🎯 识别与输入                                      [展开 ▼]  │
│ │ (折叠状态)                                                │
│                                                              │
│ 📝 字幕与文本                                      [展开 ▼]  │
│ │ (折叠状态)                                                │
│                                                              │
│ ... 更多设置组 ...                                            │
│                                                              │
│              [自动保存已启用 ✓]     [恢复默认值]              │
└──────────────────────────────────────────────────────────────┘
```

### 3.3 AXAML 结构

```xml
<!-- Views/SettingsView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui">
  <ScrollViewer>
    <StackPanel Spacing="8" Margin="24">
      <TextBlock Text="设置" FontSize="24" FontWeight="Bold" Margin="0,0,0,16"/>

      <!-- 搜索框 -->
      <TextBox Watermark="搜索设置..." Classes="search-box" Margin="0,0,0,16"/>

      <!-- 订阅与认证 -->
      <Expander Header="📡 订阅与认证" IsExpanded="True">
        <StackPanel Spacing="8" Margin="16">
          <Grid ColumnDefinitions="120,*,Auto">
            <TextBlock Text="当前订阅" VerticalAlignment="Center"/>
            <ComboBox Grid.Column="1" Items="{Binding Subscriptions}"
                      SelectedItem="{Binding ActiveSubscription}"/>
            <Button Grid.Column="2" Content="🧪 测试" Margin="8,0,0,0"/>
          </Grid>
          <!-- 更多设置项... -->
        </StackPanel>
      </Expander>

      <!-- 录音与存储 -->
      <Expander Header="🎤 录音与存储">
        <StackPanel Spacing="8" Margin="16">
          <!-- MP3 比特率、会话目录等 -->
        </StackPanel>
      </Expander>

      <!-- AI 配置 -->
      <Expander Header="🤖 AI 配置">
        <StackPanel Spacing="8" Margin="16">
          <!-- AI 提供商、端点、模型等 -->
        </StackPanel>
      </Expander>

      <!-- 更多配置组... -->
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

### 3.4 自动保存

```csharp
// 配置变更后自动保存（带防抖）
private DispatcherTimer? _saveDebounceTimer;

private void ScheduleAutoSave()
{
    _saveDebounceTimer?.Stop();
    _saveDebounceTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };
    _saveDebounceTimer.Tick += (_, _) =>
    {
        _saveDebounceTimer.Stop();
        _configService.Save(_config);
        StatusMessage = "设置已自动保存";
    };
    _saveDebounceTimer.Start();
}
```

---

## 4. 实时翻译面板优化

### 4.1 当前问题

- 翻译编辑器和历史记录并排显示，但窄屏时两者都不够宽
- 设备选择面板占用固定空间
- 「开始翻译」按钮不够醒目

### 4.2 改进方案

```
实时翻译视图:
┌─────────────────────────────────────────────────────────────┐
│ ┌── 设备快捷栏 ───────────────────────────────────────────┐ │
│ │ 🎤 [默认麦克风 ▾]  🔊 [系统音频 ▾]  🌐 中→英  [⚙ 高级] │ │
│ └───────────────────────────────────────────────────────────┘ │
│                                                               │
│ ┌── 实时显示区 ───────────────────────────────────────────┐  │
│ │                                                         │  │
│ │  [原文 | 译文 | 双语] ← SegmentedControl                │  │
│ │                                                         │  │
│ │  ┌───────────────────────────────────────────────────┐  │  │
│ │  │                                                   │  │  │
│ │  │          翻译内容编辑器                            │  │  │
│ │  │    (AdvancedRichTextBox / SimpleRichTextBox)      │  │  │
│ │  │                                                   │  │  │
│ │  │                                                   │  │  │
│ │  │                                                   │  │  │
│ │  └───────────────────────────────────────────────────┘  │  │
│ │                                                         │  │
│ │  ─── 最近翻译 ─────────────────────── [展开历史 ▼] ──── │  │
│ │  │ 14:30:21  你好世界 → Hello World                 │  │  │
│ │  │ 14:30:18  谢谢 → Thank you                      │  │  │
│ │  │ 14:30:15  再见 → Goodbye                         │  │  │
│ └───────────────────────────────────────────────────────────┘ │
│                                                               │
│ ┌── 底部操作栏 ───────────────────────────────────────────┐  │
│ │  [🔴 录音中 00:12:34]  [📌 浮动字幕]  [💾 导出字幕]    │  │
│ └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 关键改进

**4.3.1 设备选择从固定面板改为紧凑工具栏**

```xml
<!-- 紧凑设备栏: 一行即可 -->
<Border Height="40" Padding="12,0" Background="#FFF8FAFC" CornerRadius="8">
  <StackPanel Orientation="Horizontal" Spacing="16" VerticalAlignment="Center">
    <!-- 麦克风选择 -->
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="🎤" VerticalAlignment="Center"/>
      <ComboBox Items="{Binding MicrophoneDevices}"
                SelectedItem="{Binding SelectedMicrophone}"
                MinWidth="150" Height="28"/>
    </StackPanel>

    <!-- 系统音频选择 -->
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="🔊" VerticalAlignment="Center"/>
      <ComboBox Items="{Binding LoopbackDevices}"
                SelectedItem="{Binding SelectedLoopback}"
                MinWidth="150" Height="28"/>
    </StackPanel>

    <!-- 语言对选择 -->
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="🌐" VerticalAlignment="Center"/>
      <ComboBox Items="{Binding SourceLanguages}"
                SelectedItem="{Binding SelectedSourceLanguage}"
                Width="80" Height="28"/>
      <TextBlock Text="→" VerticalAlignment="Center" Margin="4,0"/>
      <ComboBox Items="{Binding TargetLanguages}"
                SelectedItem="{Binding SelectedTargetLanguage}"
                Width="80" Height="28"/>
    </StackPanel>

    <!-- 高级设置弹出 -->
    <Button Content="⚙" ToolTip.Tip="高级音频设置">
      <Button.Flyout>
        <Flyout>
          <StackPanel Spacing="8" Width="300">
            <TextBlock Text="高级音频设置" FontWeight="Bold"/>
            <CheckBox Content="启用自动增益" IsChecked="{Binding IsAgcEnabled}"/>
            <ComboBox Header="增益预设" Items="{Binding AgcPresets}"
                      SelectedItem="{Binding SelectedAgcPreset}"/>
            <!-- 更多高级设置 -->
          </StackPanel>
        </Flyout>
      </Button.Flyout>
    </Button>
  </StackPanel>
</Border>
```

**4.3.2 显示模式改用 SegmentedControl 样式**

```xml
<!-- 用 ToggleButton 组模拟 SegmentedControl -->
<Border CornerRadius="6" Background="#FFE5E7EB" Padding="2" Height="32">
  <StackPanel Orientation="Horizontal" Spacing="2">
    <ToggleButton Content="原文" Classes="segment"
                  IsChecked="{Binding IsOriginalMode}"/>
    <ToggleButton Content="译文" Classes="segment"
                  IsChecked="{Binding IsTranslatedMode}"/>
    <ToggleButton Content="双语" Classes="segment"
                  IsChecked="{Binding IsBilingualMode}"/>
  </StackPanel>
</Border>

<!-- segment 样式 -->
<Style Selector="ToggleButton.segment">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="CornerRadius" Value="4"/>
  <Setter Property="Padding" Value="12,4"/>
  <Setter Property="FontSize" Value="12"/>
  <Setter Property="BorderThickness" Value="0"/>
</Style>
<Style Selector="ToggleButton.segment:checked">
  <Setter Property="Background" Value="White"/>
  <Setter Property="FontWeight" Value="SemiBold"/>
</Style>
```

**4.3.3 历史记录改为可折叠的底部面板**

```xml
<!-- 底部可折叠历史面板 -->
<Expander Header="最近翻译" IsExpanded="{Binding IsHistoryExpanded}">
  <ListBox Items="{Binding RecentTranslations}" MaxHeight="200">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <Grid ColumnDefinitions="80,*">
          <TextBlock Grid.Column="0" Text="{Binding Timestamp}"
                     Foreground="#FF888888" FontSize="11"/>
          <TextBlock Grid.Column="1" Text="{Binding Summary}"
                     TextTrimming="CharacterEllipsis"/>
        </Grid>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>
</Expander>
```

---

## 5. 批量处理与审查面板优化

### 5.1 当前问题

- 批量处理和文件库在不同 Tab 中，操作流程不连贯
- 文件列表使用简单 ListBox，缺少排序/筛选功能
- 批量任务状态不够直观

### 5.2 改进方案：拖放式文件处理

```
批量处理视图:
┌─────────────────────────────────────────────────────────────┐
│ 批量转写                                [全部开始] [清空队列] │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│ ┌── 文件拖放区 ──────────────────────────────────────────┐  │
│ │                                                        │  │
│ │      📂  拖放音频文件到这里，或 [点击选择文件]          │  │
│ │      支持: WAV, MP3, M4A, FLAC                         │  │
│ │                                                        │  │
│ └────────────────────────────────────────────────────────┘  │
│                                                              │
│ ┌── 任务队列 ────────────────────────────────────────────┐  │
│ │ ┌────────┬──────────────┬──────┬──────────┬──────────┐ │  │
│ │ │ 状态   │ 文件名       │ 大小 │ 进度     │ 操作     │ │  │
│ │ ├────────┼──────────────┼──────┼──────────┼──────────┤ │  │
│ │ │ ✅完成 │ meeting1.wav │ 45MB │ ████████ │ [📄][🗑]│ │  │
│ │ │ ⏳处理 │ meeting2.mp3 │ 12MB │ ████░░░░ │ [⏸][🗑]│ │  │
│ │ │ ⏸等待  │ meeting3.wav │ 67MB │ ░░░░░░░░ │ [▶][🗑]│ │  │
│ │ └────────┴──────────────┴──────┴──────────┴──────────┘ │  │
│ └────────────────────────────────────────────────────────┘  │
│                                                              │
│ ┌── 统计 ────────────────────────────────────────────────┐  │
│ │ 总计: 3 个文件 | 完成: 1 | 处理中: 1 | 等待: 1        │  │
│ │ 并发数: [10 ▾]  预计剩余: ~5:30                        │  │
│ └────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### 5.3 使用 DataGrid 展示任务（替代 ListBox）

```xml
<!-- 推荐使用 Avalonia DataGrid 展示批量任务 -->
<!-- 需要引入: Avalonia.Controls.DataGrid -->
<DataGrid Items="{Binding BatchQueue}"
          AutoGenerateColumns="False"
          CanUserSortColumns="True"
          CanUserResizeColumns="True"
          IsReadOnly="True"
          GridLinesVisibility="Horizontal"
          HeadersVisibility="Column">

  <DataGrid.Columns>
    <DataGridTemplateColumn Header="状态" Width="60">
      <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
          <TextBlock Text="{Binding StatusIcon}"
                     HorizontalAlignment="Center" FontSize="16"/>
        </DataTemplate>
      </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>

    <DataGridTextColumn Header="文件名" Binding="{Binding FileName}" Width="*"/>
    <DataGridTextColumn Header="大小" Binding="{Binding FileSizeText}" Width="80"/>

    <DataGridTemplateColumn Header="进度" Width="120">
      <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
          <ProgressBar Value="{Binding Progress}" Maximum="100"
                       Height="8" CornerRadius="4"/>
        </DataTemplate>
      </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>

    <DataGridTemplateColumn Header="操作" Width="80">
      <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
          <StackPanel Orientation="Horizontal" Spacing="4">
            <Button Content="📄" ToolTip.Tip="查看结果" Width="28" Height="28"/>
            <Button Content="🗑" ToolTip.Tip="移除" Width="28" Height="28"/>
          </StackPanel>
        </DataTemplate>
      </DataGridTemplateColumn.CellTemplate>
    </DataGridTemplateColumn>
  </DataGrid.Columns>
</DataGrid>
```

### 5.4 拖放文件支持

```csharp
// BatchProcessingView.axaml.cs
private void OnDragOver(object? sender, DragEventArgs e)
{
    e.DragEffects = e.Data.Contains(DataFormats.Files)
        ? DragDropEffects.Copy
        : DragDropEffects.None;
}

private async void OnDrop(object? sender, DragEventArgs e)
{
    if (e.Data.GetFiles() is { } files)
    {
        var vm = DataContext as MainWindowViewModel;
        foreach (var file in files)
        {
            var path = file.Path.LocalPath;
            if (IsAudioFile(path))
            {
                vm?.EnqueueBatchTask(path);
            }
        }
    }
}

private static bool IsAudioFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext is ".wav" or ".mp3" or ".m4a" or ".flac" or ".ogg";
}
```

---

## 6. Media Studio 改进

### 6.1 当前问题

- Media Studio 是独立窗口（1050×720），每次打开需要新窗口
- 会话列表在左侧固定 200px，不可调节
- 聊天界面的消息气泡样式简单，图片/视频预览较小

### 6.2 改进方案

**6.2.1 集成到主窗口导航中**

```
Media Studio 内嵌视图:
┌─────────────────────────────────────────────────────────────┐
│ 🎬 Media Studio            [+ 新建会话] [🗑 删除会话]       │
├─────────────────────────────────────────────────────────────┤
│ ┌─ 会话列表 ──┐  ┌─ 会话内容 ────────────────────────────┐ │
│ │             │  │                                        │ │
│ │ 📷 风景照   │  │  👤 生成一张日落风景照                  │ │
│ │ 🎥 产品视频 │  │  ─────────────────────────────────────│ │
│ │ 📷 头像生成 │  │  🤖 已生成:                            │ │
│ │             │  │  ┌──────────────┐                      │ │
│ │             │  │  │              │                      │ │
│ │             │  │  │  [预览图片]   │  1024×1024           │ │
│ │             │  │  │              │  [📥 下载] [🔄 重新] │ │
│ │             │  │  └──────────────┘                      │ │
│ │             │  │                                        │ │
│ └─────────────┘  └────────────────────────────────────────┘ │
│ ┌── 输入区 ───────────────────────────────────────────────┐ │
│ │ [输入提示词...]                   [📎 参考图] [➤ 发送] │ │
│ │ 模式: [图片 ▾]  尺寸: [1024×1024 ▾]  质量: [标准 ▾]   │ │
│ └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

**6.2.2 使用 SplitView 实现可调节的会话列表**

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="220" MinWidth="150" MaxWidth="350"/>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="*"/>
  </Grid.ColumnDefinitions>

  <!-- 会话列表 -->
  <ListBox Grid.Column="0" Items="{Binding Sessions}"
           SelectedItem="{Binding SelectedSession}">
    <ListBox.ItemTemplate>
      <DataTemplate>
        <StackPanel Spacing="2" Margin="4">
          <TextBlock Text="{Binding Title}" FontWeight="SemiBold"/>
          <TextBlock Text="{Binding LastMessage}" FontSize="11"
                     Foreground="#FF888888" TextTrimming="CharacterEllipsis"/>
        </StackPanel>
      </DataTemplate>
    </ListBox.ItemTemplate>
  </ListBox>

  <!-- 拖拽分割线 -->
  <GridSplitter Grid.Column="1" Width="4" Background="Transparent"
                ResizeBehavior="PreviousAndNext"/>

  <!-- 会话内容 -->
  <Grid Grid.Column="2" RowDefinitions="*,Auto">
    <!-- 消息列表 -->
    <ScrollViewer Grid.Row="0">
      <ItemsControl Items="{Binding CurrentMessages}">
        <!-- 消息模板... -->
      </ItemsControl>
    </ScrollViewer>

    <!-- 输入区 -->
    <Border Grid.Row="1" Padding="12" Background="#FFF8FAFC">
      <!-- 输入框和参数设置 -->
    </Border>
  </Grid>
</Grid>
```

**6.2.3 增强的图片预览**

```xml
<!-- 图片消息模板 - 更大的预览 + 操作按钮 -->
<DataTemplate x:Key="ImageMessageTemplate">
  <Border Classes="ai-msg" Margin="0,4" Padding="12" CornerRadius="12"
          MaxWidth="500">
    <StackPanel Spacing="8">
      <TextBlock Text="已生成图片:" FontSize="12" Foreground="#FF666666"/>

      <!-- 图片预览 -->
      <Border CornerRadius="8" ClipToBounds="True"
              Cursor="Hand" PointerPressed="OnImageClicked">
        <Image Source="{Binding ImagePath, Converter={StaticResource PathToBitmap}}"
               MaxWidth="400" MaxHeight="400"
               Stretch="Uniform"/>
      </Border>

      <!-- 元信息和操作 -->
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Grid.Column="0" Text="{Binding ImageInfo}"
                   FontSize="11" Foreground="#FF888888"/>
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4">
          <Button Content="📥 下载" Classes="compact"/>
          <Button Content="🔄 重新生成" Classes="compact"/>
          <Button Content="🖼 预览" Classes="compact"/>
        </StackPanel>
      </Grid>
    </StackPanel>
  </Border>
</DataTemplate>
```

---

## 7. 状态反馈系统升级

### 7.1 当前问题

- 状态信息只通过底部文字显示（`StatusMessage`），容易被忽视
- 长时间操作（如批量转写）缺少进度百分比
- 成功/失败/警告没有视觉区分
- 订阅验证状态用小圆点表示，不够直观

### 7.2 改进方案

**7.2.1 InfoBar 通知系统**

```xml
<!-- 顶部 InfoBar - 可自动消失的通知 -->
<Panel>
  <!-- 成功通知 -->
  <Border IsVisible="{Binding ShowSuccessInfo}" Padding="12,8"
          Background="#FFE8F5E9" CornerRadius="8" Margin="16,8"
          HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*,Auto">
      <TextBlock Grid.Column="0" Text="✅" Margin="0,0,8,0"/>
      <TextBlock Grid.Column="1" Text="{Binding InfoMessage}"/>
      <Button Grid.Column="2" Content="✕" Classes="ghost"
              Command="{Binding DismissInfoCommand}"/>
    </Grid>
  </Border>

  <!-- 警告通知 -->
  <Border IsVisible="{Binding ShowWarningInfo}" Padding="12,8"
          Background="#FFFFF3E0" CornerRadius="8" Margin="16,8">
    <Grid ColumnDefinitions="Auto,*,Auto">
      <TextBlock Grid.Column="0" Text="⚠️" Margin="0,0,8,0"/>
      <TextBlock Grid.Column="1" Text="{Binding WarningMessage}"/>
      <Button Grid.Column="2" Content="✕" Classes="ghost"/>
    </Grid>
  </Border>

  <!-- 错误通知 -->
  <Border IsVisible="{Binding ShowErrorInfo}" Padding="12,8"
          Background="#FFFFEBEE" CornerRadius="8" Margin="16,8">
    <Grid ColumnDefinitions="Auto,*,Auto">
      <TextBlock Grid.Column="0" Text="❌" Margin="0,0,8,0"/>
      <TextBlock Grid.Column="1" Text="{Binding ErrorMessage}"/>
      <Button Grid.Column="2" Content="✕" Classes="ghost"/>
    </Grid>
  </Border>
</Panel>
```

**7.2.2 进度指示增强**

```xml
<!-- 全局操作进度条 -->
<ProgressBar IsVisible="{Binding IsGlobalBusy}"
             IsIndeterminate="{Binding IsProgressIndeterminate}"
             Value="{Binding GlobalProgress}"
             Maximum="100" Height="3"
             DockPanel.Dock="Top"/>
```

**7.2.3 ViewModel 通知辅助方法**

```csharp
public partial class MainWindowViewModel
{
    // 通知类型
    public enum NotificationType { Success, Warning, Error, Info }

    [ObservableProperty] private bool _showSuccessInfo;
    [ObservableProperty] private bool _showWarningInfo;
    [ObservableProperty] private bool _showErrorInfo;
    [ObservableProperty] private string _infoMessage = "";
    [ObservableProperty] private string _warningMessage = "";
    [ObservableProperty] private string _errorMessage = "";

    private CancellationTokenSource? _notificationDismissCts;

    private async Task ShowNotificationAsync(string message,
        NotificationType type, int durationMs = 5000)
    {
        _notificationDismissCts?.Cancel();
        _notificationDismissCts = new CancellationTokenSource();

        // 重置所有通知
        ShowSuccessInfo = ShowWarningInfo = ShowErrorInfo = false;

        switch (type)
        {
            case NotificationType.Success:
                InfoMessage = message;
                ShowSuccessInfo = true;
                break;
            case NotificationType.Warning:
                WarningMessage = message;
                ShowWarningInfo = true;
                break;
            case NotificationType.Error:
                ErrorMessage = message;
                ShowErrorInfo = true;
                break;
        }

        // 自动消失
        try
        {
            await Task.Delay(durationMs, _notificationDismissCts.Token);
            ShowSuccessInfo = ShowWarningInfo = ShowErrorInfo = false;
        }
        catch (OperationCanceledException) { }
    }
}
```

---

## 8. 键盘快捷键与无障碍

### 8.1 当前问题

- 除了 F2 重命名，几乎没有键盘快捷键
- 没有 TabIndex 定义，键盘导航顺序不确定
- 缺少 AutomationProperties 辅助信息
- 焦点指示器不明显

### 8.2 改进方案

**8.2.1 全局快捷键定义**

```xml
<!-- MainWindow.axaml - 快捷键绑定 -->
<Window.KeyBindings>
  <!-- 核心操作 -->
  <KeyBinding Gesture="F5" Command="{Binding StartTranslationCommand}"/>
  <KeyBinding Gesture="F6" Command="{Binding StopTranslationCommand}"/>
  <KeyBinding Gesture="Ctrl+Shift+S" Command="{Binding ExportSubtitleCommand}"/>

  <!-- 导航 -->
  <KeyBinding Gesture="Ctrl+1" Command="{Binding NavigateCommand}"
              CommandParameter="0"/>  <!-- 实时翻译 -->
  <KeyBinding Gesture="Ctrl+2" Command="{Binding NavigateCommand}"
              CommandParameter="1"/>  <!-- 批量处理 -->
  <KeyBinding Gesture="Ctrl+3" Command="{Binding NavigateCommand}"
              CommandParameter="2"/>  <!-- AI 洞察 -->
  <KeyBinding Gesture="Ctrl+4" Command="{Binding NavigateCommand}"
              CommandParameter="3"/>  <!-- Media Studio -->

  <!-- 工具 -->
  <KeyBinding Gesture="Ctrl+OemComma" Command="{Binding OpenSettingsCommand}"/>
  <KeyBinding Gesture="F1" Command="{Binding OpenHelpCommand}"/>
  <KeyBinding Gesture="Ctrl+N" Command="{Binding NewSessionCommand}"/>
</Window.KeyBindings>
```

**8.2.2 快捷键提示**

```xml
<!-- 在按钮 ToolTip 中显示快捷键 -->
<Button Content="▶ 开始翻译"
        ToolTip.Tip="开始实时翻译 (F5)"
        Command="{Binding StartTranslationCommand}"/>

<Button Content="⏹ 停止"
        ToolTip.Tip="停止翻译 (F6)"
        Command="{Binding StopTranslationCommand}"/>
```

**8.2.3 无障碍属性**

```xml
<!-- 为关键控件添加 AutomationProperties -->
<ComboBox Items="{Binding MicrophoneDevices}"
          AutomationProperties.Name="麦克风设备选择"
          AutomationProperties.HelpText="选择用于语音识别的麦克风设备"/>

<Button Command="{Binding StartTranslationCommand}"
        AutomationProperties.Name="开始翻译"
        AutomationProperties.AcceleratorKey="F5"/>

<Ellipse Fill="{Binding SubscriptionLampColor}"
         AutomationProperties.Name="{Binding SubscriptionStatusText}"/>
```

### 8.3 快捷键速查表

| 快捷键 | 功能 | 场景 |
|--------|------|------|
| `F5` | 开始翻译 | 全局 |
| `F6` | 停止翻译 | 全局 |
| `Ctrl+1` ~ `Ctrl+4` | 切换导航页面 | 全局 |
| `Ctrl+,` | 打开设置 | 全局 |
| `Ctrl+Shift+S` | 导出字幕 | 翻译页面 |
| `Ctrl+N` | 新建会话 | Media Studio |
| `F1` | 帮助 | 全局 |
| `F2` | 重命名 | 会话列表 |
| `Ctrl+F` | 搜索 | 编辑器 |
| `Escape` | 取消/关闭 | 对话框/弹出 |

---

## 9. 深色模式与主题系统

### 9.1 当前状态

- 使用 FluentTheme，自动跟随系统深色/浅色设置
- 自定义样式中大量硬编码颜色值（如 `#FFF8FAFC`、`#FFE5E7EB`）
- 深色模式下自定义颜色可能不协调

### 9.2 改进方案

**9.2.1 使用动态资源替代硬编码颜色**

```xml
<!-- App.axaml - 定义语义化颜色资源 -->
<Application.Resources>
  <ResourceDictionary>
    <!-- 浅色主题默认值 -->
    <Color x:Key="SurfaceColor">#FFF8FAFC</Color>
    <Color x:Key="SurfaceAltColor">#FFF1F5F9</Color>
    <Color x:Key="BorderSubtleColor">#FFE5E7EB</Color>
    <Color x:Key="PrimaryColor">#FF2563EB</Color>
    <Color x:Key="PrimaryHoverColor">#FF1D4ED8</Color>
    <Color x:Key="SuccessColor">#FF16A34A</Color>
    <Color x:Key="WarningColor">#FFFFA000</Color>
    <Color x:Key="ErrorColor">#FFE53935</Color>
    <Color x:Key="TextPrimaryColor">#FF1A1A1A</Color>
    <Color x:Key="TextSecondaryColor">#FF666666</Color>
    <Color x:Key="TextMutedColor">#FF888888</Color>

    <SolidColorBrush x:Key="SurfaceBrush" Color="{DynamicResource SurfaceColor}"/>
    <SolidColorBrush x:Key="PrimaryBrush" Color="{DynamicResource PrimaryColor}"/>
    <!-- ... 更多语义化画刷 -->
  </ResourceDictionary>
</Application.Resources>
```

**9.2.2 替换硬编码引用**

```xml
<!-- ❌ 改造前 -->
<Border Background="#FFF8FAFC">
<TextBlock Foreground="#FF888888">

<!-- ✅ 改造后 -->
<Border Background="{DynamicResource SurfaceBrush}">
<TextBlock Foreground="{DynamicResource TextMutedBrush}">
```

**9.2.3 深色主题覆盖**

```xml
<!-- Themes/Dark.axaml -->
<ResourceDictionary>
  <Color x:Key="SurfaceColor">#FF1E1E2E</Color>
  <Color x:Key="SurfaceAltColor">#FF2A2A3C</Color>
  <Color x:Key="BorderSubtleColor">#FF3A3A4C</Color>
  <Color x:Key="PrimaryColor">#FF60A5FA</Color>
  <Color x:Key="TextPrimaryColor">#FFEEEEEE</Color>
  <Color x:Key="TextSecondaryColor">#FFAAAAAA</Color>
  <Color x:Key="TextMutedColor">#FF888888</Color>
</ResourceDictionary>
```

---

## 10. 响应式布局

### 10.1 当前问题

- 最小窗口 1240×600 偏大，笔记本电脑（1366×768）几乎全屏
- 面板大小固定，不适应不同屏幕尺寸
- 侧边栏不可折叠

### 10.2 改进方案

**10.2.1 降低最小尺寸**

```xml
<!-- 从 1240×600 降低到 1000×550 -->
<Window MinWidth="1000" MinHeight="550">
```

**10.2.2 自适应侧边栏**

```xml
<!-- SplitView 支持紧凑和展开模式 -->
<SplitView DisplayMode="CompactInline"
           IsPaneOpen="{Binding IsSidebarOpen}"
           CompactPaneLength="48"
           OpenPaneLength="200">
  <!-- 紧凑模式只显示图标，展开模式显示图标+文字 -->
</SplitView>
```

**10.2.3 基于窗口宽度调整布局**

```csharp
// 在 MainWindow.axaml.cs 中监听窗口大小变化
protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
{
    base.OnPropertyChanged(e);

    if (e.Property == BoundsProperty)
    {
        var width = Bounds.Width;
        if (DataContext is MainWindowViewModel vm)
        {
            // 窄屏: 自动折叠侧边栏
            vm.IsSidebarOpen = width > 1200;

            // 超窄屏: 切换到紧凑布局
            vm.IsCompactMode = width < 1000;
        }
    }
}
```

---

## 11. 微交互与动效

### 11.1 翻译状态动画

```xml
<!-- 翻译中的脉冲动画 -->
<Style Selector="Ellipse.recording-indicator">
  <Style.Animations>
    <Animation Duration="0:0:1.5" IterationCount="INFINITE">
      <KeyFrame Cue="0%">
        <Setter Property="Opacity" Value="1"/>
      </KeyFrame>
      <KeyFrame Cue="50%">
        <Setter Property="Opacity" Value="0.3"/>
      </KeyFrame>
      <KeyFrame Cue="100%">
        <Setter Property="Opacity" Value="1"/>
      </KeyFrame>
    </Animation>
  </Style.Animations>
</Style>

<!-- 使用 -->
<Ellipse Classes="recording-indicator" Width="8" Height="8"
         Fill="Red" IsVisible="{Binding IsTranslating}"/>
```

### 11.2 页面切换过渡

```xml
<!-- ContentControl 过渡动画 -->
<ContentControl Content="{Binding CurrentView}">
  <ContentControl.ContentTransition>
    <CrossFade Duration="0:0:0.2"/>
  </ContentControl.ContentTransition>
</ContentControl>
```

### 11.3 按钮反馈

```xml
<!-- 按钮按下缩放效果 -->
<Style Selector="Button.interactive:pressed">
  <Setter Property="RenderTransform">
    <ScaleTransform ScaleX="0.97" ScaleY="0.97"/>
  </Setter>
</Style>

<!-- 按钮悬停发光 -->
<Style Selector="Button.primary:pointerover">
  <Setter Property="BoxShadow" Value="0 2 8 0 #40000000"/>
</Style>
```

---

## 12. 新增功能组件建议

### 12.1 全局搜索（Command Palette）

```
按 Ctrl+K 打开全局搜索:
┌──────────────────────────────────────┐
│ 🔍 搜索命令或设置...                 │
│                                      │
│ 最近使用                             │
│   🎤 开始翻译                        │
│   ⚙️ 打开设置                        │
│   📋 批量处理                        │
│                                      │
│ 命令                                 │
│   📌 切换浮动字幕                     │
│   💾 导出字幕                        │
│   🎬 新建 Media Studio 会话          │
│                                      │
│ 设置                                 │
│   🎤 麦克风设备                       │
│   🌐 语言设置                        │
│   🤖 AI 配置                        │
└──────────────────────────────────────┘
```

### 12.2 欢迎页面/引导

```
首次启动引导:
┌─────────────────────────────────────────────────────┐
│                                                     │
│      🎤 欢迎使用译见 Pro                            │
│                                                     │
│  开始之前，请完成以下设置:                           │
│                                                     │
│  [1] ✅ 添加 Azure 订阅                             │
│      ────────────────────                           │
│  [2] ⬜ 选择翻译语言                                │
│      ────────────────────                           │
│  [3] ⬜ 配置音频设备                                │
│      ────────────────────                           │
│  [4] ⬜ 设置 AI 服务（可选）                         │
│                                                     │
│                          [跳过]  [开始配置 →]       │
└─────────────────────────────────────────────────────┘
```

### 12.3 会话时间线

```
翻译历史时间线视图:
┌───────────────────────────────────────────────────┐
│ 📅 2026-02-24 会议记录                            │
│                                                   │
│ 14:30 ─── 会议开始 ─────────────────────────      │
│ │                                                 │
│ 14:30:05  张三: "今天我们讨论项目进度"             │
│ │         → "Today we discuss project progress"   │
│ │                                                 │
│ 14:30:15  李四: "进度有些延迟"                     │
│ │         → "Progress is slightly delayed"        │
│ │                                                 │
│ 14:32 ─── AI 洞察：检测到进度延迟 ──────          │
│ │                                                 │
│ 14:35:22  张三: "需要调整时间表"                   │
│ │         → "Need to adjust the schedule"         │
│                                                   │
│ 15:00 ─── 会议结束 ─────────────────────────      │
└───────────────────────────────────────────────────┘
```

---

## 实施路线图

### 第一阶段：快速见效（1-2 周）

- [ ] 将设备选择面板从固定行改为紧凑工具栏（节省垂直空间）
- [ ] 为翻译显示模式添加 SegmentedControl 样式
- [ ] 添加全局快捷键（F5 开始/F6 停止）
- [ ] 增加 InfoBar 式通知（成功/警告/错误）
- [ ] 添加翻译状态脉冲动画
- [ ] 降低最小窗口尺寸到 1000×550

### 第二阶段：布局重组（2-4 周）

- [ ] 实现侧边栏导航（SplitView）
- [ ] 将 MainWindow.axaml 拆分为独立 UserControl 视图
- [ ] 将配置中心从弹窗改为内嵌设置页
- [ ] 将 Media Studio 从独立窗口改为内嵌视图
- [ ] 页面切换过渡动画

### 第三阶段：功能增强（4-6 周）

- [ ] 批量处理改用 DataGrid + 拖放支持
- [ ] 语义化颜色资源（支持深色模式自适应）
- [ ] 响应式侧边栏（窗口宽度自适应折叠）
- [ ] 完善键盘导航和无障碍属性
- [ ] 全局搜索（Command Palette）

### 第四阶段：精细打磨

- [ ] 首次启动引导（Onboarding）
- [ ] 会话时间线视图
- [ ] 更丰富的微交互动效
- [ ] 用户可自定义的布局偏好
- [ ] A/B 测试不同布局方案
