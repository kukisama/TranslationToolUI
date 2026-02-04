# 下一阶段待处理事项：音频设备选择 + 自定义 Chunk + 边翻译边录音

日期：2026-02-04

## 背景与目标

当前语音输入走 Azure Speech SDK 默认麦克风（`AudioConfig.FromDefaultMicrophoneInput()`）。
下一阶段目标：

1) 引入 NAudio 以枚举/选择实际可用音频设备（不仅限麦克风，包含系统回环）。
2) 支持可配置的推流 chunk 时长（例如默认 200ms，允许用户改到 500ms）。
3) 点击“开始翻译”时同时开启本地录音：文件名带时间戳，优先输出 MP3（体积更小），并保证不影响实时推送。

## 设计原则（需要贯穿实现）

- **Speech 推流格式必须严格一致**：声明给 Speech SDK 的 `AudioStreamFormat` 必须与写入 `PushAudioInputStream.Write(...)` 的字节流格式完全一致。
- **识别与录音分离**：识别端可以固定转为 16kHz/mono/PCM16；录音端可以尽量保留原始采集格式以保证质量。
- **避免阻塞音频回调**：NAudio `DataAvailable` 回调中只做轻量入队/复制；重采样/编码/磁盘写入放后台线程。
- **跨平台保底**：在非 Windows 或 NAudio 不可用时，回退到默认麦克风输入（不崩溃）。

## 里程碑（建议顺序）

- M1：设备枚举 + 选择 + 识别推流打通（Windows）
- M2：chunk 时长可配置 + 稳定缓冲（不会越跑越延迟）
- M3：边翻译边录音（WAV 稳定落地）
- M4：停止时转 MP3（或实时 MP3，如可行）+ UI/配置完善

## 待办清单（可逐项勾选）

### A. 现状梳理与入口点确认

- [x] 确认开始/停止翻译命令的入口（`MainWindowViewModel` 中的 `RelayCommand` 绑定）
  说明：入口为 `StartTranslationCommand`/`StopTranslationCommand`，由 `MainWindowViewModel` 调用 `SpeechTranslationService.StartTranslationAsync()`/`StopTranslationAsync()`。
- [x] 确认 `SpeechTranslationService` 的创建/启动/停止生命周期（确保能挂载录音 Start/Stop）
  说明：已在 `SpeechTranslationService` 内部挂载音频采集、PushStream 与录音的 Start/Stop，并在停止时统一清理资源。
- [x] 确认配置模型与 JSON 持久化位置（`AzureSpeechConfig` + `ConfigurationService`）
  说明：音频/录音相关字段已加入 `AzureSpeechConfig`，仍由 `ConfigurationService` 写入同一份 config.json。

### B. 依赖引入（Windows 音频）

- [x] 引入 NAudio（建议 `NAudio` + `NAudio.Wasapi`，按 NuGet 实际拆分情况选择）
  说明：项目已添加 NuGet 包 `NAudio`，用于 WASAPI 设备枚举与采集。
- [x] 评估并选择 MP3 编码依赖：Windows Media Foundation
  说明：停止翻译后使用 `NAudio.MediaFoundation.MediaFoundationEncoder.EncodeToMp3(...)` 异步转码生成 MP3。

### C. 音频设备枚举与选择（Windows）

- [x] 新增“音频源模式”枚举：`DefaultMic` / `CaptureDevice` / `Loopback`
  说明：已新增 `AudioSourceMode`（JsonStringEnumConverter），并写入配置与 UI。
- [x] 枚举设备列表（WASAPI）
  说明：已实现 Capture/Render（Loopback 选择 Render 设备）枚举，列表可刷新。
- [x] 设备项字段：`DeviceId`、`DisplayName`、`Type`（Capture/Render）
  说明：已新增 `AudioDeviceInfo`/`AudioDeviceType`，并在 UI 下拉框展示名称。
- [x] UI：配置页下拉框选择设备 + “刷新”按钮
  说明：配置页已增加“音频来源 + 设备选择 + 刷新”，并在非 Windows 上自动禁用。
- [x] 配置持久化：保存 `SelectedAudioDeviceId` + `AudioSourceMode`
  说明：保存时写回 `AzureSpeechConfig.AudioSourceMode/SelectedAudioDeviceId`。

### D. 识别推流（PushAudioInputStream）

- [x] 在 `SpeechTranslationService` 中改为使用 PushStream
  说明：已创建 `AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1)` + `CreatePushStream`，并用 `AudioConfig.FromStreamInput(pushStream)` 启动 `TranslationRecognizer`。
- [x] 明确线程模型与停止顺序
  说明：WASAPI 回调仅写入缓冲；后台读取线程按 `ChunkDurationMs` 组 chunk 触发事件并写入 PushStream；停止时会释放采集、`pushStream.Close()` 并 Dispose `AudioConfig`。

### E. 格式转换管线（关键风险点）

环回/部分设备常见格式：48kHz、立体声、float32。
Speech 推流建议：16kHz、单声道、PCM16。

- [x] 48kHz -> 16kHz 下采样（NAudio `WdlResamplingSampleProvider`）
  说明：采集后通过 SampleProvider 管线重采样到 16k。
- [x] stereo -> mono 混音（L/R 各 0.5）
  说明：2 声道使用 `StereoToMonoSampleProvider`，左右各 0.5。
- [x] float32 -> PCM16（`SampleToWaveProvider16`）
  说明：最终输出为 `16k/mono/PCM16` 字节流，用于识别推流与录音。
- [x] blockAlign 对齐
  说明：chunk 字节数按 `WaveFormat.BlockAlign` 对齐。

### F. Chunk 时长可配置（用户可调）

- [x] 新增配置：`ChunkDurationMs`（默认 200）
  说明：已加入 `AzureSpeechConfig.ChunkDurationMs` 并用于切块。
- [x] UI：数值输入，范围 20–2000ms
  说明：配置页已加入 `ChunkDurationMs` 数值框并保存。
- [x] 推流切块逻辑基于该配置
  说明：按 `OutputWaveFormat.AverageBytesPerSecond` 计算 chunk 字节数并对齐。
- [x] 缓冲策略：队列有界
  说明：录音写入使用有界 Channel（满了丢最旧），避免内存增长与延迟累积。

### G. 边翻译边录音（时间戳文件名）

目标：点击“开始翻译”后同时录音，停止翻译时结束录音。

- [x] 新增配置：`EnableRecording`、`RecordingMp3BitrateKbps`、`DeleteWavAfterMp3`
  说明：已在配置页提供开关与码率设置，转码成功后按配置删除 WAV。
- [x] 生成文件名：`yyyyMMdd_HHmmss`
  说明：每次开始翻译生成独立时间戳，文件名为 `Audio_yyyyMMdd_HHmmss.wav/mp3`，避免多次会话互相覆盖。
- [x] 录音写入器设计：实时写 WAV，停止后异步转 MP3
  说明：录音实时写入 WAV；停止翻译后后台任务转 MP3（转码失败会保留 WAV 并提示）。
- [x] 录音与识别“分流（tee）”策略
  说明：同一份 `PCM16 chunk` 同时写入 Speech PushStream 与 WAV 写入队列，互不阻塞。

### H. “原始格式直推”高级选项（可选）

你提出保留“原始格式直推”主要用于录音；如果也希望用于识别，需要非常谨慎。

- [ ] 若做直推：必须把 `AudioStreamFormat` 声明为与采集输出一致的 PCM（通常不是 float），否则识别会异常。
- [ ] 建议默认仅用于“录音保真”，识别仍固定走 16k/mono/PCM16（更稳更可控）。

## 关注点 / 风险提示

- Windows-only：NAudio（WASAPI）主要在 Windows 上工作；跨平台需做降级或后续引入跨平台音频层。
- MP3 依赖与可用性：
  - Media Foundation 在不同系统/精简版 Windows 上可能不可用或编码器受限。
  - NAudio.Lame 需要你确认分发与许可。
- 性能与延迟：重采样与编码都可能吃 CPU；需避免回调阻塞与队列无限增长。
- 停止流程：必须正确停止采集/线程并 `Close()` PushStream，避免识别器挂起等待。

## 验收标准（完成后应达到）

- 可在 UI 中看到设备列表并选择（含 Loopback），开始翻译后确实来自所选设备。
- chunk 默认 200ms，可改为 500ms，延迟/稳定性变化符合预期且不会越跑越慢。
- 勾选录音后，开始翻译即生成录音文件；停止翻译后文件可正常播放、时长与会话匹配。
- 非 Windows（或禁用相关功能）时，应用仍可用默认麦克风路径工作，不崩溃。

## 修改日志

- 2026-02-04：完成 NAudio 设备枚举/选择、16k/mono/PCM16 推流、ChunkDurationMs 配置与 UI。
- 2026-02-04：完成边翻译边录音（WAV 实时写入，停止后异步转 MP3，成功后按配置删除 WAV）。
- 2026-02-04：修复无声问题：`BufferedWaveProvider.ReadFully=false`，避免持续向 PushStream 写入补零静音。
- 2026-02-04：修复录音文件异常：每次开始翻译生成独立音频文件名，停止时先排空写入再转码，避免并发覆盖导致 MP3 时长异常。
- 2026-02-04：新增订阅可用性异步验证：实现 `AzureSubscriptionValidator`（issueToken 验证），主窗/配置页复用同一套验证逻辑与消息返回。
- 2026-02-04：主窗口新增订阅状态指示灯：验证中空心闪烁，成功绿灯，失败红灯；切换订阅自动重新验证，并在底部状态栏显示提示消息。
- 2026-02-04：主窗口加入音频快捷设置：把“音频来源/设备/刷新”从配置页迁移到首页；移除“发现几个设备”等提示文字，保留刷新与禁用逻辑（非 Windows 自动回退）。
- 2026-02-04：主窗口布局多轮压缩调整：源/目标语言移到顶部与工具按钮同排；音频行标签统一为“输入”；刷新按钮位置调整；订阅行与音频行合并为一行节省高度。
- 2026-02-04：新增“帮助”下拉菜单与“关于”窗口：帮助菜单包含 Azure Speech 服务入口、GitHub 地址、关于；关于窗口展示项目简介与模块说明。
- 2026-02-04：主窗口控制最小尺寸避免控件折行：设置窗口 `MinWidth`/`MinHeight`，确保顶部按钮组不被挤压折叠。
- 2026-02-04：翻译开始/停止按钮改为互斥单按钮：新增 `ToggleTranslationCommand` 与按钮外观/文案绑定（开始=绿色，停止=红色），并将该按钮与翻译状态开关一起下移到第二行右侧。

- 2026-02-04：接入应用 Logo/图标资源：关于窗口仅展示原始 PNG 大图（保留项目说明文字），不再在帮助菜单等位置到处塞图标，避免 UI 过度装饰。
- 2026-02-04：修复“窗口图标在 XAML 中直接构造 WindowIcon”导致的编译/运行问题：改为运行期用安全加载器设置 `Window.Icon`，资源缺失时吞异常保证应用可启动。
- 2026-02-04（失败经验）：尝试用 MSBuild `CodeTaskFactory` 在构建期直接把 PNG 转 ICO，因 .NET Core/MSBuild 环境兼容性限制而失败；结论：图标生成应放到独立工具/脚本里。
- 2026-02-04（失败经验）：尝试用 `System.Drawing` 生成多尺寸 ICO（net8.0-windows + UseSystemDrawing），在当前环境缺少相关类型/引用导致构建失败；结论：不要让“图标生成小功能”引入脆弱的系统依赖链。
- 2026-02-04：回退到无依赖 PNG→ICO 包装写法并增加自证日志（输入 PNG 尺寸 + sha256、输出大小），用于确认“每次构建确实读取了当前 PNG”。注意：Windows Explorer 可能因图标缓存/缩放策略造成“看起来像旧图标”。
- 2026-02-04：按“允许引入外部依赖但不耦合生产代码”的原则，将 PNG→ICO 转换封装为独立可执行工具 `tools/IconGen`；主程序不引用该依赖，工具缺失/失败时构建只告警不阻塞（应用仍可正常运行）。
- 2026-02-04：`IconGen` 引入 `SixLabors.ImageSharp` 生成真正多尺寸 ICO（16/32/48/256），并采用“居中补成正方形 + 最近邻缩放”以兼顾像素风与透明边。
- 2026-02-04：修复图标生成“写入后移动失败/文件占用”问题：生成输出改为写临时文件后关闭句柄再 `Move` 覆盖；同时每次生成到 `obj\\Generated\\AppIcon.{GUID}.ico`，避免覆盖同名文件时被占用导致失败。
- 2026-02-04：补齐“预先约定位置”落盘：构建期生成后会尽力复制/覆盖到 `Assets\\AppIcon.ico`（失败不影响 build），便于仓库内查看与后续发布使用。
- 2026-02-04：中文路径可用性排查：文件 IO 在 Unicode 路径下可正常读写；控制台日志出现乱码主要是终端/MSBuild 捕获输出的编码问题，工具侧已尽力设置 `Console.OutputEncoding=UTF8`（不影响实际生成结果）。

- 2026-02-04：处理“帮助/关于打不开：Unsupported IBinding implementation 'Markdown.Avalonia.Extensions.StaticBinding'”（复杂问题，过程记录）
  - 触发方式：点击主窗“帮助 → 帮助/关于”。
  - 表象一：窗口不弹；状态栏可能只显示“打开失败: Unsupported IBinding...StaticBinding”。
  - 表象二：`dotnet run` 终端常只显示 build 成功，最后 `exit=1`，但不打印异常（原因：应用是 `WinExe`，控制台/Debug 输出不稳定，且异常可能发生在 UI 渲染/布局阶段）。
  - 第一次尝试（失败）：假设是“默认编译绑定”与第三方控件不兼容，只在 About/Help 的 `<Window>` 上设置 `x:CompileBindings="False"`，用户侧仍复现同样异常。
  - 第二次尝试（定位手段）：引入“稳定可复现的日志”——新增 `CrashLogger`（小功能）。
    - 做法：在 `Program.Main` 启动时初始化；在 `App.OnFrameworkInitializationCompleted` 挂接 UI 线程未处理异常；并把 trace/异常写入文件。
    - 文件：调试时写到 `bin/Debug/net8.0/logs/trace.log`，终端不显示也能直接打开文件看堆栈。
    - 代码位置：`Services/CrashLogger.cs`（初始化点在 `Program.cs` / `App.axaml.cs`）。
    - 为什么“现在能打印到终端”了：
      - 之前的现象（看起来像“没报错/只 exit=1”）主要来自：应用是 `WinExe`，很多情况下没有控制台窗口；`Console.WriteLine/Debug.WriteLine` 未必能被 `dotnet run` 捕获或显示。
      - 现在的策略是：
        - 把异常写入 `Console.Error`（stderr），`dotnet run` 会稳定把 stderr 打到当前终端。
        - 同时把异常与 Avalonia Trace 落到文件（`TextWriterTraceListener` → `trace.log`），即使终端不显示也能回放。
        - `Program.BuildAvaloniaApp().LogToTrace()` 会把 Avalonia 的日志走 `System.Diagnostics.Trace`，因此 trace 文件里能同时看到我们的日志与框架日志。
      - 结果：终端可看到关键堆栈；文件里也能完整保留堆栈（便于截图/发 issue/回溯）。
      - 常用的一行复现/采样命令（PowerShell）：
        - `Remove-Item -Force -Recurse "...\\bin\\Debug\\net8.0\\logs" -ErrorAction SilentlyContinue`：
          - 目的：删除上一次运行遗留的日志目录，保证这次运行产出的 `trace.log` 是“干净的、和本次复现一一对应”。
          - `-ErrorAction SilentlyContinue`：目录不存在时不报错，便于反复执行。
        - `dotnet run --project "...\\TranslationToolUI.csproj" -c Debug -v minimal`：
          - 目的：在 Debug 配置下构建并启动应用；`-v minimal` 让 MSBuild 输出足够的信息但不过载。
          - 关键点：即使应用是 `WinExe`，`dotnet run` 仍会把子进程写到 stdout/stderr 的内容带回当前终端；配合我们把异常写到 `Console.Error`，就能稳定看到堆栈。
        - `Write-Host "dotnet run exit=$LASTEXITCODE"`：
          - 目的：明确打印 `dotnet run` 的退出码，区分“正常退出(0)”与“异常退出(非0)”。
          - `$LASTEXITCODE` 是 PowerShell 记录“上一条外部程序”退出码的变量。
        - `Get-Content "...\\trace.log" -Tail 60`：
          - 目的：直接在终端展示日志文件最后 60 行（通常包含最新异常/堆栈）。
          - 这样做的好处：即使终端没捕获到运行时输出，文件里仍有完整 trace，可以即时回显出来。
      - 额外提醒：如果遇到 `MSB3026/MSB3027`（复制/覆盖 exe 失败），通常是旧的 `TranslationToolUI.exe` 还在运行占用文件；需要先结束进程再执行上述命令。
    - 额外注意：Windows 上如果 `TranslationToolUI.exe` 正在运行，`dotnet run`/`dotnet build` 可能会出现 `MSB3026/MSB3027`（复制 apphost/覆盖 exe 失败）。排查时先确保旧进程退出再跑。
  - 证据（trace 关键点）：堆栈进入 `Avalonia.Data.Core.MultiBindingExpression.StartCore()`，抛出 `NotSupportedException: Unsupported IBinding implementation 'Markdown.Avalonia.Extensions.StaticBinding'`，发生在 Markdown 控件测量/布局（`ColorTextBlock.Avalonia`）阶段。
  - 结论：并非 `About.md` 某句文本“格式不对”导致编译失败；而是 `Markdown.Avalonia` 在我们当前 Avalonia 版本/绑定管线组合下运行时不兼容（至少在本工程内可稳定复现）。
  - 升级尝试（受限/失败）：尝试把 `Markdown.Avalonia` 升级到与 Avalonia 11.3.0 对齐的版本，但 NuGet 上不存在 `Markdown.Avalonia 11.3.0`（可用版本不匹配）。
  - 最终处置（可用性优先）：移除 `Markdown.Avalonia` 依赖，About/Help 改为只读文本显示（仍从 `About.md/Help.md` 加载，发布后替换文件即可更新内容）。验证：`dotnet run exit=0`，不再因 StaticBinding 异常退出。
  - 影响与后续：暂时失去 Markdown 富文本渲染；后续若找到兼容的 Markdown 渲染方案（或自研最小渲染子集）再恢复。

### 2026-02-04：交流记录摘要（用于留痕）

说明：此段记录我们在“图标生成/帮助与关于文档可维护性/运行时兼容性”上的来回讨论点与最终决策，包含失败过程与后续注意事项。

- 约束确认：允许引入外部依赖，但必须满足“小功能、与生产代码不耦合、工具独立可执行、主程序没有它也能工作正常”。
- 自测要求：在中文路径下必须能 `dotnet build`，并能证明 ICO 每次构建确实从当前 PNG 生成，且能覆盖到预先约定位置 `Assets\\AppIcon.ico`。
- 失败路线（记录原因）：
  - 试图用 MSBuild `CodeTaskFactory` 直接做 PNG→ICO，因 .NET Core/MSBuild 环境兼容限制失败。
  - 试图用 `System.Drawing` 生成 ICO，因类型/引用与运行环境差异导致构建失败或脆弱。
  - 结论：图标生成应放到“独立工具（可选步骤）”，并在 MSBuild 中 best-effort 调用。
- 最终落地：采用 `tools\\IconGen`（ImageSharp）生成多尺寸 ICO，构建时输出到 `obj\\Generated\\AppIcon.{GUID}.ico` 避免锁；并 best-effort 复制到 `Assets\\AppIcon.ico` 作为“约定位置”。
- 文档可更新性：帮助/关于改为 Markdown（内置资源 + 发布目录同名文件覆盖优先），避免以后改文案要改代码再发版。
- 运行时问题与定位：点击“帮助/关于”出现 `Unsupported IBinding implementation 'Markdown.Avalonia.Extensions.StaticBinding'`，导致窗口无法弹出。
  - 第一次修复尝试：在 `Views\\AboutView.axaml` 与 `Views\\HelpView.axaml` 设置 `x:CompileBindings=\"False\"`（只对 Markdown 窗口禁用编译绑定），但用户反馈仍可复现同样报错。
  - 后续结论：通过 `CrashLogger` 的 trace 确认根因是 `Markdown.Avalonia` 的 `StaticBinding` 与当前绑定管线不兼容；最终回退为纯文本展示，确保稳定。

例子： Remove-Item -Force -Recurse "c:\原D\Scripts\Avalonia\03-16-3清理代码，准备上github\TranslationToolUI\bin\Debug\net8.0\logs" -ErrorAction SilentlyContinue; dotnet run --project "c:\原D\Scripts\Avalonia\03-16-3清理代码，准备上github\TranslationToolUI\TranslationToolUI.csproj" -c Debug -v minimal; Write-Host "dotnet run exit=$LASTEXITCODE"; if (Test-Path "c:\原D\Scripts\Avalonia\03-16-3清理代码，准备上github\TranslationToolUI\bin\Debug\net8.0\logs\trace.log") { Get-Content "c:\原D\Scripts\Avalonia\03-16-3清理代码，准备上github\TranslationToolUI\bin\Debug\net8.0\logs\trace.log" -Tail 60 }
