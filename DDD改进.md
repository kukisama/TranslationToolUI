# 译见 Pro — DDD 驱动的 AI 集成改进方案

> 目标：以领域驱动设计（DDD）方法论，指导"译见 Pro"在 **当前语言（C#）、当前框架（Avalonia/.NET 10）** 基础上，系统性地集成 AI 能力并持续改进架构，同时保持跨平台可行性。
>
> 原则：**可落地、可复现、可检查**——每一步都有明确的输入、输出与验收标准。

---

## 目录

1. [现状评估](#1-现状评估)
2. [DDD 战略设计](#2-ddd-战略设计)
3. [DDD 战术设计](#3-ddd-战术设计)
4. [AI 集成模式](#4-ai-集成模式)
5. [跨平台考量](#5-跨平台考量)
6. [分阶段实施路线图](#6-分阶段实施路线图)
7. [验收检查清单](#7-验收检查清单)
8. [附录：方法论参考](#8-附录方法论参考)

---

## 1. 现状评估

### 1.1 技术栈

| 项 | 当前状态 |
|---|---|
| 语言 | C# / .NET 10 |
| UI 框架 | Avalonia 11.3（跨平台桌面） |
| 语音/翻译 | Azure Speech SDK 1.44 |
| AI/LLM | OpenAI Compatible + Azure OpenAI（`AiInsightService`） |
| 音频采集 | NAudio + WASAPI（Windows 为主） |
| 配置持久化 | JSON 文件（`ConfigurationService`） |
| 媒体生成 | DALL-E / Sora（`AiImageGenService`、`AiVideoGenService`） |

### 1.2 架构现状

```
┌────────────────────────────────────────────────┐
│                 Views (AXAML)                   │
├────────────────────────────────────────────────┤
│         ViewModels (9 个 partial class)          │
│   MainWindowViewModel.*.cs                     │
│   MediaStudioViewModel / FloatingSubtitle...   │
├────────────────────────────────────────────────┤
│              Services (21 个文件)                │
│   SpeechTranslationService                     │
│   AiInsightService / AiImageGen / AiVideoGen   │
│   ConfigurationService / PathManager           │
│   Audio/ (WASAPI, Recorder, Transcoder)        │
├────────────────────────────────────────────────┤
│              Models (21 个文件)                  │
│   AzureSpeechConfig / AiConfig / MediaGenConfig│
│   TranslationItem / SubtitleCue / BatchTask    │
└────────────────────────────────────────────────┘
```

### 1.3 识别到的改进点

| 编号 | 问题 | DDD 视角 | 影响 |
|------|------|----------|------|
| P1 | **无接口抽象** — 所有 Service 均为具体类 | 缺少端口（Port）定义 | 无法替换/Mock/测试 |
| P2 | **手动 new 实例化** — ViewModel 直接 `new SpeechTranslationService(...)` | 无依赖注入 | 耦合度高，切换实现困难 |
| P3 | **AzureSpeechConfig 过于庞大** — 222 行，混合 Speech / AI / Audio / Batch 配置 | 聚合根边界模糊 | 改一个配置项影响面大 |
| P4 | **Models 是贫血模型** — `TranslationItem` 仅有属性，无领域行为 | 缺少领域对象 | 业务逻辑散落在 ViewModel |
| P5 | **领域事件用回调** — `Action<string> onChunk` 而非领域事件 | 缺少事件驱动模式 | 事件消费者与生产者耦合 |
| P6 | **跨平台能力有限** — WASAPI、Media Foundation 仅 Windows | 缺少平台抽象层 | 无法真正跨平台运行 |

---

## 2. DDD 战略设计

### 2.1 统一语言（Ubiquitous Language）

> DDD 第一步：团队（包括与 AI 协作）使用一致的领域术语。

| 领域术语 | 英文 | 定义 |
|----------|------|------|
| **会话** | Session | 一次从开始到结束的翻译/转录活动 |
| **翻译条目** | TranslationEntry | 一段被识别和翻译的文本单元 |
| **字幕线索** | SubtitleCue | 带时间戳的字幕项 |
| **洞察** | Insight | AI 对会话内容的分析结果 |
| **复盘** | Review | 结构化的会议总结（含结论、行动项、风险点） |
| **订阅凭证** | Subscription | Azure 服务的认证信息 |
| **音频源** | AudioSource | 音频输入的抽象（麦克风/回环/设备） |
| **媒体工坊** | MediaStudio | 图片/视频的 AI 生成工作台 |
| **生成任务** | GenTask | 一次图片或视频生成请求 |

### 2.2 限界上下文（Bounded Contexts）

基于现有代码和领域分析，划分为 **5 个限界上下文**：

```
┌─────────────────────────────────────────────────────────────────┐
│                        译见 Pro 系统                              │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐   │
│  │  语音翻译上下文 │  │  AI 洞察上下文 │  │   媒体生成上下文      │   │
│  │  Speech &     │  │  Insight &   │  │   Media Generation  │   │
│  │  Translation  │  │  Review      │  │                     │   │
│  │              │  │              │  │                     │   │
│  │ - 实时翻译    │  │ - 智能问答    │  │ - 图片生成           │   │
│  │ - 语音识别    │  │ - 会议复盘    │  │ - 视频生成           │   │
│  │ - 字幕同步    │  │ - 预设模板    │  │ - 任务管理           │   │
│  │ - 批量转录    │  │              │  │                     │   │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘   │
│         │                 │                      │               │
│  ┌──────┴─────────────────┴──────────────────────┴───────────┐   │
│  │                    共享内核 (Shared Kernel)                  │   │
│  │  - 配置管理（Subscription / AiConfig / PathManager）         │   │
│  │  - 认证服务（AzureTokenProvider）                           │   │
│  │  - 音频基础设施（AudioSource 抽象）                          │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              表现层（Presentation Context）                 │   │
│  │  - Views / ViewModels / Commands                         │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.3 上下文映射（Context Map）

| 上游 | 下游 | 关系模式 | 说明 |
|------|------|----------|------|
| 语音翻译 | AI 洞察 | **客户-供应商** | 翻译结果作为洞察的输入 |
| 语音翻译 | 表现层 | **开放主机服务 (OHS)** | 通过领域事件发布翻译结果 |
| AI 洞察 | 表现层 | **开放主机服务 (OHS)** | 流式输出 Insight 内容 |
| 共享内核 | 所有上下文 | **共享内核** | 配置与认证 |
| Azure SDK | 语音翻译 | **防腐层 (ACL)** | 隔离 Azure Speech SDK 的类型 |
| OpenAI API | AI 洞察 | **防腐层 (ACL)** | 隔离 LLM API 协议细节 |

---

## 3. DDD 战术设计

### 3.1 目标目录结构

```
TranslationToolUI/
├── Domain/                          # 领域层（纯 C#，无框架依赖）
│   ├── SpeechTranslation/           # 语音翻译上下文
│   │   ├── Entities/
│   │   │   └── TranslationSession.cs
│   │   ├── ValueObjects/
│   │   │   ├── TranslationEntry.cs
│   │   │   └── SubtitleCue.cs
│   │   ├── Events/
│   │   │   ├── TranslationRecognized.cs
│   │   │   └── SessionCompleted.cs
│   │   ├── Services/
│   │   │   └── SubtitleFormatter.cs
│   │   └── Ports/
│   │       ├── ISpeechRecognizer.cs
│   │       └── IAudioSource.cs
│   │
│   ├── AiInsight/                   # AI 洞察上下文
│   │   ├── Entities/
│   │   │   └── InsightConversation.cs
│   │   ├── ValueObjects/
│   │   │   ├── InsightQuery.cs
│   │   │   └── ReviewReport.cs
│   │   ├── Events/
│   │   │   └── InsightGenerated.cs
│   │   └── Ports/
│   │       └── ILlmProvider.cs
│   │
│   ├── MediaGeneration/             # 媒体生成上下文
│   │   ├── Entities/
│   │   │   ├── MediaSession.cs
│   │   │   └── GenTask.cs
│   │   ├── ValueObjects/
│   │   │   └── MediaArtifact.cs
│   │   ├── Events/
│   │   │   └── TaskStatusChanged.cs
│   │   └── Ports/
│   │       ├── IImageGenerator.cs
│   │       └── IVideoGenerator.cs
│   │
│   └── Shared/                      # 共享内核
│       ├── ValueObjects/
│       │   ├── SubscriptionCredential.cs
│       │   └── AiProviderConfig.cs
│       └── Ports/
│           ├── ITokenProvider.cs
│           └── IConfigRepository.cs
│
├── Infrastructure/                  # 基础设施层（具体实现）
│   ├── Azure/
│   │   ├── AzureSpeechRecognizer.cs       # 实现 ISpeechRecognizer
│   │   ├── AzureTokenProvider.cs          # 实现 ITokenProvider
│   │   └── AzureBatchTranscriber.cs
│   ├── OpenAi/
│   │   └── OpenAiLlmProvider.cs           # 实现 ILlmProvider
│   ├── Audio/
│   │   ├── WasapiAudioSource.cs           # Windows 实现 IAudioSource
│   │   └── PulseAudioSource.cs            # Linux 实现 IAudioSource（未来）
│   ├── Persistence/
│   │   └── JsonConfigRepository.cs        # 实现 IConfigRepository
│   └── Media/
│       ├── DallEImageGenerator.cs         # 实现 IImageGenerator
│       └── SoraVideoGenerator.cs          # 实现 IVideoGenerator
│
├── Application/                     # 应用服务层（编排领域对象）
│   ├── SpeechTranslation/
│   │   └── TranslationAppService.cs
│   ├── AiInsight/
│   │   └── InsightAppService.cs
│   └── MediaGeneration/
│       └── MediaGenAppService.cs
│
├── ViewModels/                      # 表现层（保持现有结构）
├── Views/
├── Controls/
└── Assets/
```

### 3.2 关键领域对象设计

#### 3.2.1 值对象（Value Object）示例

```csharp
// Domain/SpeechTranslation/ValueObjects/TranslationEntry.cs
namespace TranslationToolUI.Domain.SpeechTranslation.ValueObjects;

/// <summary>
/// 一段翻译条目（不可变值对象）
/// </summary>
public sealed record TranslationEntry(
    DateTime Timestamp,
    string OriginalText,
    string TranslatedText,
    bool IsFinal)
{
    /// <summary>判断是否为空条目</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(OriginalText)
                        && string.IsNullOrWhiteSpace(TranslatedText);

    /// <summary>生成双语显示文本</summary>
    public string ToBilingualText(string separator = " | ")
        => $"{OriginalText}{separator}{TranslatedText}";

    /// <summary>生成字幕时间戳格式</summary>
    public string ToTimestampLabel()
        => Timestamp.ToString("HH:mm:ss");
}
```

**检查点**：值对象应不可变（`record`）、自验证、包含领域行为。

#### 3.2.2 实体（Entity）示例

```csharp
// Domain/SpeechTranslation/Entities/TranslationSession.cs
namespace TranslationToolUI.Domain.SpeechTranslation.Entities;

/// <summary>
/// 翻译会话（实体，有唯一标识和生命周期）
/// </summary>
public class TranslationSession
{
    public string Id { get; }
    public DateTime StartedAt { get; }
    public DateTime? EndedAt { get; private set; }
    public SessionStatus Status { get; private set; }

    private readonly List<TranslationEntry> _entries = new();
    public IReadOnlyList<TranslationEntry> Entries => _entries.AsReadOnly();

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public TranslationSession(string id)
    {
        Id = id;
        StartedAt = DateTime.UtcNow;
        Status = SessionStatus.Active;
    }

    /// <summary>追加翻译条目</summary>
    public void AddEntry(TranslationEntry entry)
    {
        if (Status != SessionStatus.Active)
            throw new InvalidOperationException("会话已结束，无法追加条目");

        _entries.Add(entry);

        if (entry.IsFinal)
            _domainEvents.Add(new TranslationRecognized(Id, entry));
    }

    /// <summary>结束会话</summary>
    public void Complete()
    {
        EndedAt = DateTime.UtcNow;
        Status = SessionStatus.Completed;
        _domainEvents.Add(new SessionCompleted(Id, _entries.Count));
    }

    /// <summary>获取全部文本（用于 AI 洞察输入）</summary>
    public string ToFullTranscript(bool includeTimestamps = true)
    {
        return string.Join("\n", _entries
            .Where(e => e.IsFinal)
            .Select(e => includeTimestamps
                ? $"[{e.ToTimestampLabel()}] {e.ToBilingualText()}"
                : e.ToBilingualText()));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public enum SessionStatus { Active, Completed, Cancelled }
```

**检查点**：实体通过 Id 唯一标识，封装行为（`AddEntry`/`Complete`），产生领域事件。

#### 3.2.3 端口（Port / 接口）示例

```csharp
// Domain/SpeechTranslation/Ports/ISpeechRecognizer.cs
namespace TranslationToolUI.Domain.SpeechTranslation.Ports;

/// <summary>
/// 语音识别端口 — 领域层定义，基础设施层实现
/// </summary>
public interface ISpeechRecognizer
{
    Task StartAsync(SpeechRecognizerOptions options, CancellationToken ct);
    Task StopAsync();
    bool IsRunning { get; }

    /// <summary>识别到翻译结果</summary>
    event EventHandler<TranslationEntry> EntryRecognized;

    /// <summary>状态变化</summary>
    event EventHandler<RecognizerStatus> StatusChanged;
}

// Domain/AiInsight/Ports/ILlmProvider.cs
namespace TranslationToolUI.Domain.AiInsight.Ports;

/// <summary>
/// LLM 服务端口 — 支持流式输出
/// </summary>
public interface ILlmProvider
{
    IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userContent,
        LlmRequestOptions options,
        CancellationToken ct);
}

// Domain/Shared/Ports/IConfigRepository.cs
namespace TranslationToolUI.Domain.Shared.Ports;

/// <summary>
/// 配置仓储端口
/// </summary>
public interface IConfigRepository
{
    Task<TConfig> LoadAsync<TConfig>() where TConfig : class, new();
    Task SaveAsync<TConfig>(TConfig config) where TConfig : class;
}
```

**检查点**：端口定义在领域层，不依赖任何具体技术（Azure SDK / HttpClient 等）。

#### 3.2.4 领域事件（Domain Event）

```csharp
// Domain/SpeechTranslation/Events/TranslationRecognized.cs
namespace TranslationToolUI.Domain.SpeechTranslation.Events;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

public sealed record TranslationRecognized(
    string SessionId,
    TranslationEntry Entry) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record SessionCompleted(
    string SessionId,
    int TotalEntries) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

### 3.3 应用服务层示例

```csharp
// Application/AiInsight/InsightAppService.cs
namespace TranslationToolUI.Application.AiInsight;

/// <summary>
/// AI 洞察应用服务 — 编排领域对象，不包含业务规则
/// </summary>
public class InsightAppService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IConfigRepository _configRepo;

    public InsightAppService(ILlmProvider llmProvider, IConfigRepository configRepo)
    {
        _llmProvider = llmProvider;
        _configRepo = configRepo;
    }

    /// <summary>
    /// 对翻译会话内容进行 AI 洞察分析
    /// </summary>
    public async IAsyncEnumerable<string> AnalyzeSessionAsync(
        TranslationSession session,
        string question,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var config = await _configRepo.LoadAsync<AiConfig>();
        var transcript = session.ToFullTranscript();

        var systemPrompt = config.InsightSystemPrompt;
        var userContent = config.InsightUserContentTemplate
            .Replace("{history}", transcript)
            .Replace("{question}", question);

        await foreach (var chunk in _llmProvider.StreamChatAsync(
            systemPrompt, userContent,
            new LlmRequestOptions { Profile = AiChatProfile.Quick }, ct))
        {
            yield return chunk;
        }
    }
}
```

---

## 4. AI 集成模式

### 4.1 六边形架构（Ports & Adapters）与 AI

```
                    ┌───────────────────────────┐
       用户操作      │      应用服务层             │      AI 服务
    ─────────────►  │  （编排领域逻辑）            │  ◄────────────
                    │                           │
       ViewModel    │   ┌───────────────────┐   │   ILlmProvider
    ◄──────────────►│   │   领域层（纯逻辑）   │   │◄──────────────►
                    │   │                   │   │   ISpeechRecognizer
       UI Port      │   │  TranslationSession│   │   IAudioSource
                    │   │  InsightConversation│  │   IImageGenerator
                    │   └───────────────────┘   │
                    └───────────────────────────┘
                                │
                    ┌───────────┴───────────────┐
                    │     基础设施适配器           │
                    │  AzureSpeechRecognizer     │
                    │  OpenAiLlmProvider         │
                    │  DallEImageGenerator       │
                    │  WasapiAudioSource         │
                    │  JsonConfigRepository      │
                    └───────────────────────────┘
```

### 4.2 防腐层（Anti-Corruption Layer）

当前 `AiInsightService` 直接依赖 OpenAI HTTP 协议细节（SSE 流式解析、JSON 结构等）。DDD 要求通过防腐层隔离外部模型：

```csharp
// Infrastructure/OpenAi/OpenAiLlmProvider.cs
namespace TranslationToolUI.Infrastructure.OpenAi;

/// <summary>
/// 防腐层：将 OpenAI API 协议转换为领域层 ILlmProvider 接口
/// </summary>
public class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly AiConfig _config;
    private readonly ITokenProvider? _tokenProvider;

    public OpenAiLlmProvider(AiConfig config, ITokenProvider? tokenProvider = null)
    {
        _config = config;
        _tokenProvider = tokenProvider;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userContent,
        LlmRequestOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. 构建请求（OpenAI 协议细节封装在此）
        var request = BuildChatRequest(systemPrompt, userContent, options);
        await SetAuthHeaderAsync(request);

        // 2. 发送流式请求
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // 3. 解析 SSE 流 → 转换为领域层的 string chunks
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;
            if (line == "data: [DONE]") yield break;

            var content = ExtractContentDelta(line[6..]);
            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    // ... 协议细节方法（BuildChatRequest, SetAuthHeaderAsync, ExtractContentDelta）
}
```

### 4.3 AI 能力扩展点

利用 DDD 端口模式，可以轻松扩展新的 AI 能力：

| 扩展场景 | 端口接口 | 适配器实现 |
|----------|----------|-----------|
| 切换到本地 LLM（Ollama） | `ILlmProvider` | `OllamaLlmProvider` |
| 替换为 Whisper 本地识别 | `ISpeechRecognizer` | `WhisperSpeechRecognizer` |
| 增加 AI 翻译后编辑 | `ITranslationPostEditor` | `LlmTranslationPostEditor` |
| 增加 AI 实时摘要 | `IRealtimeSummarizer` | `LlmRealtimeSummarizer` |
| 本地图片生成（Stable Diffusion） | `IImageGenerator` | `StableDiffusionGenerator` |
| 语音合成（TTS） | `ISpeechSynthesizer` | `AzureTtsProvider` |

### 4.4 与 AI 协作开发的具体方法

> 利用 AI（如 GitHub Copilot、ChatGPT）加速 DDD 落地：

#### 4.4.1 AI 辅助领域建模

```
Prompt 模板：
"我有一个翻译工具，核心功能是 [实时语音翻译/AI 洞察/媒体生成]。
请帮我识别这个子域中的实体、值对象和聚合根，并给出 C# 代码骨架。
约束：
- 领域层不依赖任何框架
- 使用 record 定义值对象
- 实体通过 Id 标识
- 聚合根管理领域事件"
```

#### 4.4.2 AI 辅助接口提取

```
Prompt 模板：
"以下是现有的 [SpeechTranslationService] 具体实现。
请帮我：
1. 提取出领域层接口（Port）
2. 将现有实现包装为基础设施层适配器
3. 保持向后兼容，不破坏现有功能"
```

#### 4.4.3 AI 辅助测试生成

```
Prompt 模板：
"为以下领域对象生成单元测试：
- TranslationSession.AddEntry() 的正常/异常场景
- TranslationEntry 值对象的不可变性验证
- InsightAppService 使用 Mock ILlmProvider 的集成测试
使用 xUnit + NSubstitute，遵循 Arrange-Act-Assert 模式"
```

---

## 5. 跨平台考量

### 5.1 平台抽象策略

当前 Windows 专属组件及跨平台替代方案：

| 组件 | Windows 实现 | Linux/macOS 替代 | 抽象接口 |
|------|-------------|------------------|----------|
| 音频采集 | `WasapiPcm16AudioSource` | PulseAudio / CoreAudio | `IAudioSource` |
| 设备枚举 | `AudioDeviceEnumerator`（MMDevice） | PulseAudio / AVFoundation | `IAudioDeviceEnumerator` |
| MP3 转码 | `WavToMp3Transcoder`（MF） | FFmpeg / LAME CLI | `IAudioTranscoder` |
| 高质量录音 | `HighQualityRecorder`（NAudio） | PortAudio / ALSA | `IAudioRecorder` |

### 5.2 条件编译 + DI 注册

```csharp
// Program.cs 或 DI 配置
public static void RegisterPlatformServices(IServiceCollection services)
{
    // 音频源
    if (OperatingSystem.IsWindows())
    {
        services.AddTransient<IAudioSource, WasapiAudioSource>();
        services.AddTransient<IAudioTranscoder, MediaFoundationTranscoder>();
    }
    else if (OperatingSystem.IsLinux())
    {
        services.AddTransient<IAudioSource, PulseAudioSource>();
        services.AddTransient<IAudioTranscoder, FfmpegTranscoder>();
    }
    else if (OperatingSystem.IsMacOS())
    {
        services.AddTransient<IAudioSource, CoreAudioSource>();
        services.AddTransient<IAudioTranscoder, FfmpegTranscoder>();
    }

    // 跨平台服务
    services.AddSingleton<ILlmProvider, OpenAiLlmProvider>();
    services.AddSingleton<ISpeechRecognizer, AzureSpeechRecognizer>();
    services.AddSingleton<IConfigRepository, JsonConfigRepository>();
    services.AddSingleton<ITokenProvider, AzureTokenProvider>();
}
```

### 5.3 Avalonia 跨平台最佳实践

| 实践 | 说明 |
|------|------|
| 避免 `System.Windows` | 使用 Avalonia 对应 API |
| 文件路径 | 使用 `Path.Combine()` 和 `Environment.SpecialFolder` |
| 运行时检测 | `OperatingSystem.IsWindows()` 等标准 API |
| 条件包引用 | `.csproj` 中用 `Condition="$([MSBuild]::IsOSPlatform('Windows'))"` |
| UI 线程调度 | 使用 `Dispatcher.UIThread` 而非 `SynchronizationContext` |

---

## 6. 分阶段实施路线图

### 阶段一：接口提取与依赖反转（2-3 周）

> 最低风险、最高收益——不改变行为，只提取抽象。

| 步骤 | 操作 | 验收标准 |
|------|------|----------|
| 1.1 | 为 `SpeechTranslationService` 提取 `ISpeechRecognizer` 接口 | 编译通过，现有功能不变 |
| 1.2 | 为 `AiInsightService` 提取 `ILlmProvider` 接口 | 编译通过，AI 洞察功能不变 |
| 1.3 | 为 `ConfigurationService` 提取 `IConfigRepository` 接口 | 编译通过，配置加载/保存不变 |
| 1.4 | 为 `AzureTokenProvider` 提取 `ITokenProvider` 接口 | 编译通过，认证流程不变 |
| 1.5 | 引入 `Microsoft.Extensions.DependencyInjection`，在 `App.axaml.cs` 配置容器 | ViewModel 通过构造函数注入获取服务 |

**操作方法（可用 AI 辅助）**：

```bash
# 步骤 1.1 示例：提取接口
# 1. 在 IDE 中打开 SpeechTranslationService.cs
# 2. 使用 Refactor → Extract Interface（或 AI 生成）
# 3. 将接口文件移至 Domain/SpeechTranslation/Ports/
# 4. 将原实现移至 Infrastructure/Azure/
# 5. 更新 ViewModel 中的引用为接口类型
# 6. 运行 dotnet build 验证
```

### 阶段二：领域模型重构（3-4 周）

> 将贫血模型升级为富领域模型。

| 步骤 | 操作 | 验收标准 |
|------|------|----------|
| 2.1 | 创建 `Domain/` 目录结构 | 目录层次符合 3.1 设计 |
| 2.2 | 将 `TranslationItem` 重构为 `TranslationEntry`（值对象） | 不可变，包含 `ToBilingualText()` 等行为 |
| 2.3 | 创建 `TranslationSession` 实体 | 封装条目管理和会话生命周期 |
| 2.4 | 拆分 `AzureSpeechConfig` 为独立配置值对象 | 每个上下文有自己的配置类 |
| 2.5 | 实现领域事件（`TranslationRecognized`、`SessionCompleted`） | 事件被正确发布和消费 |
| 2.6 | 为领域对象编写单元测试 | 覆盖核心行为，测试通过 |

**检查点**：`Domain/` 目录下的代码不应有任何对 `Azure`、`NAudio`、`HttpClient`、`Avalonia` 的 `using`。

### 阶段三：AI 能力增强（2-3 周）

> 基于端口模式扩展 AI 功能。

| 步骤 | 操作 | 验收标准 |
|------|------|----------|
| 3.1 | 实现 `ILlmProvider` 的 Ollama 本地适配器 | 可切换到本地 LLM 运行 |
| 3.2 | 增加 AI 翻译后编辑端口 `ITranslationPostEditor` | 翻译结果经 LLM 润色后显示 |
| 3.3 | 增加实时摘要端口 `IRealtimeSummarizer` | 翻译过程中实时生成摘要 |
| 3.4 | 增加术语表端口 `IGlossaryProvider` | AI 翻译使用领域术语表 |

### 阶段四：跨平台音频（3-4 周）

> 实现非 Windows 平台的音频支持。

| 步骤 | 操作 | 验收标准 |
|------|------|----------|
| 4.1 | 实现 `IAudioSource` 的 PulseAudio 适配器 | Linux 上可采集音频 |
| 4.2 | 实现 `IAudioTranscoder` 的 FFmpeg 适配器 | Linux/macOS 上可转码 |
| 4.3 | 平台条件注册（DI） | 各平台自动选择正确实现 |
| 4.4 | CI 多平台构建验证 | `dotnet build` 在 Windows/Linux/macOS 通过 |

---

## 7. 验收检查清单

### 7.1 架构层面

- [ ] **接口覆盖率**：核心服务（语音识别、LLM、配置、认证、音频）均有对应的端口接口
- [ ] **领域纯净度**：`Domain/` 目录下无任何基础设施依赖（`using Azure.*`、`using NAudio`、`using System.Net.Http` 等）
- [ ] **依赖方向**：所有依赖方向从外向内（`Infrastructure → Domain`，`Application → Domain`，`ViewModels → Application`）
- [ ] **编译验证**：`dotnet build` 成功，无警告（或仅有预期警告如 ICO 生成）

### 7.2 功能层面

- [ ] **实时翻译**：语音识别 → 翻译 → 显示的完整链路正常工作
- [ ] **AI 洞察**：基于翻译历史的 AI 问答功能正常
- [ ] **会议复盘**：生成结构化 Markdown 复盘报告正常
- [ ] **媒体生成**：图片/视频生成功能正常
- [ ] **配置持久化**：配置保存/加载正常，向后兼容旧配置文件

### 7.3 测试层面

- [ ] **领域对象单元测试**：`TranslationSession`、`TranslationEntry`、`InsightConversation` 等核心对象有测试
- [ ] **端口/适配器集成测试**：`ILlmProvider` 的 Mock 实现可替换真实服务
- [ ] **回归测试**：现有功能在重构后无退化

### 7.4 跨平台层面

- [ ] **Windows**：完整功能可用（WASAPI + MF）
- [ ] **Linux**：核心功能可用（语音识别 + AI 洞察；音频采集有适配器）
- [ ] **macOS**：核心功能可用（同上）
- [ ] **CI 构建**：`dotnet build` 在三个平台通过

### 7.5 DDD 合规检查（代码审查要点）

```bash
# 检查领域层是否引用了基础设施
grep -r "using Azure\." Domain/              # 应无结果
grep -r "using NAudio" Domain/               # 应无结果
grep -r "using System.Net.Http" Domain/      # 应无结果
grep -r "using Avalonia" Domain/             # 应无结果

# 检查接口是否定义在领域层
find Domain/ -name "I*.cs" | wc -l           # 应 ≥ 5

# 检查基础设施层是否实现了领域接口
grep -r ": ISpeechRecognizer" Infrastructure/ # 应有结果
grep -r ": ILlmProvider" Infrastructure/      # 应有结果

# 检查 DI 注册完整性
grep -r "services.Add" Program.cs App.axaml.cs # 应覆盖所有端口
```

---

## 8. 附录：方法论参考

### 8.1 核心方法论

| 方法论 | 适用范围 | 在本项目中的应用 |
|--------|----------|----------------|
| **DDD 战略设计** | 划分限界上下文 | 语音翻译 / AI 洞察 / 媒体生成三大上下文 |
| **DDD 战术设计** | 领域对象建模 | 实体、值对象、聚合、领域事件 |
| **六边形架构** | 依赖反转 | 端口（接口）+ 适配器（实现） |
| **CQRS（轻量级）** | 读写分离 | 配置读取 vs 配置保存可用不同模型 |
| **事件驱动** | 上下文间通信 | 翻译完成 → 触发 AI 洞察 |
| **绞杀者模式** | 渐进式重构 | 新功能用 DDD，旧功能逐步迁移 |

### 8.2 AI 辅助开发工具链

| 工具 | 用途 |
|------|------|
| GitHub Copilot | 代码补全、接口提取、测试生成 |
| ChatGPT / Claude | 领域建模对话、架构决策讨论 |
| Cursor / VS Code + AI | 批量重构、代码迁移 |
| AI Code Review | PR 审查，检查 DDD 合规性 |

### 8.3 参考书目与资源

| 资源 | 说明 |
|------|------|
| 《领域驱动设计》Eric Evans | DDD 奠基之作 |
| 《实现领域驱动设计》Vaughn Vernon | 战术实现指南 |
| 《整洁架构》Robert C. Martin | 依赖反转、边界设计 |
| Microsoft 的 DDD 参考架构 | [eShopOnContainers](https://github.com/dotnet-architecture/eShopOnContainers) |
| Avalonia 跨平台指南 | [docs.avaloniaui.net](https://docs.avaloniaui.net) |

### 8.4 渐进式迁移策略（绞杀者模式）

> 不做大规模重写，而是渐进替换。

```
迭代 1: 提取接口 → 原实现保持不变 → 通过 DI 注入
迭代 2: 新功能用 DDD 方式实现 → 旧功能继续运行
迭代 3: 逐步将旧 Service 迁移到 Infrastructure/ 适配器
迭代 4: 清理旧代码 → 领域层完全独立

每个迭代：
  ✅ 编译通过
  ✅ 现有功能正常
  ✅ 新增测试覆盖变更点
```

---

## 总结

本方案以 **DDD 战略设计** 划分限界上下文（语音翻译、AI 洞察、媒体生成），以 **六边形架构** 定义端口与适配器，以 **渐进式绞杀者模式** 确保安全迁移。每个阶段都有明确的操作步骤、代码示例和验收检查清单，支持用 AI 工具辅助落地。

关键收益：
1. **可测试性**：领域层纯逻辑，可独立单元测试
2. **可替换性**：AI 服务、语音引擎均可通过端口替换
3. **可扩展性**：新增 AI 能力只需添加端口 + 适配器
4. **跨平台性**：平台差异通过 DI + 适配器隔离
5. **可维护性**：统一语言 + 清晰边界 → 降低认知负担
