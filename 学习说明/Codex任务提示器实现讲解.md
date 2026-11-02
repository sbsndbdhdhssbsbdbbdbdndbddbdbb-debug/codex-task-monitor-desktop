# Codex 任务提示器：从想法到桌面浮窗

这份笔记只用于学习。`学习说明` 文件夹没有被项目文件引用，因此不会参与编译、不会进入已经发布的程序，也不会改变当前的功能。

## 1. 先用一句话理解它

这个程序不是“控制 Codex”的程序，而是一个**观察者**：它读取 Codex 已经写在本机的任务记录，推断每个任务的状态，然后用 Windows 窗口、通知和桌宠把结果展示出来。

可以把整件事想成学校图书馆的借阅系统：

1. Codex 是正在工作的同学。
2. Codex 的本地记录是借阅登记册，记录“开始、调用工具、等待回答、完成”等事件。
3. 监控器是管理员，持续读登记册并判断每个人现在在做什么。
4. 桌宠是门口的电子看板，告诉你谁正在忙、谁在等你。
5. 点击一项任务，等于通过任务的唯一编号回到对应的 Codex 对话。

核心数据流如下：

```text
Codex 本地 state 数据库 / 会话 JSONL
            │
            ▼
ThreadCatalogReader 找到有哪些对话
            │
            ▼
IncrementalJsonlReader 只读取新增事件
            │
            ▼
SessionEventParser 把 JSON 转成统一事件
            │
            ▼
TaskStateMachine 推断任务状态
            │
            ▼
CodexMonitorService 保存快照、发出变化通知
            │
       ┌────┴─────┐
       ▼          ▼
主窗口 / Toast   DesktopPetViewModel
                     │
                     ▼
                 桌面悬浮窗
```

## 2. 为什么能知道 Codex 在做什么？

### 2.1 不靠屏幕截图，而是读取本地记录

Codex Desktop 会在用户目录下保存线程目录、SQLite 状态数据库和会话 JSONL 文件。JSONL 可以理解为“每行都是一个 JSON 小事件”的流水账。

程序先尝试从 Codex 的 SQLite 状态库读取任务目录；这一步由 [ThreadCatalogReader.cs](../src/CodexTaskMonitor.Core/ThreadCatalogReader.cs) 完成。目录记录里有：

- `ThreadId`：任务/对话的唯一身份证号。
- `rollout_path`：这段对话对应 JSONL 流水账的位置。
- 标题、项目路径、最近更新时间。
- 是否属于子代理（subagent）。

如果数据库被占用或未来版本的结构变了，它会退回到扫描 JSONL 文件。这叫做**降级方案**：主路径更快，备用路径更耐用。

### 2.2 为什么可以在 Codex 正在写文件时读取？

读取会话文件时使用了：

```csharp
FileShare.ReadWrite | FileShare.Delete
```

这表示“我只读，并允许原来的 Codex 继续写入或替换文件”。监控器不会反过来锁住 Codex。

这也是它不会污染 Codex 数据的关键：程序以只读方式观察；它自己的配置和缓存另存于 `%LOCALAPPDATA%\CodexTaskMonitor`。

## 3. 从事件到状态：状态机是什么？

最重要的概念是**状态机**。大学阶段可以把它想成一个有限集合的状态图：同一时刻，一个任务只能处于有限几种状态之一；遇到某类事件，就按规则跳到另一个状态。

程序的状态大致是：

```text
Idle（空闲）
  └─ 收到新任务 / task_started ─► Running（进行中）
                                      ├─ request_user_input ─► NeedsInput（等待你）
                                      ├─ task_complete ──────► Completed（完成）
                                      ├─ turn_error ─────────► Failed（失败）
                                      └─ 太久无活动 ─────────► Stale（可能卡住）
```

真实规则位于 [TaskStateMachine.cs](../src/CodexTaskMonitor.Core/TaskStateMachine.cs)。它的 `Apply` 方法接收一个解析后的事件，再返回新的任务快照。

例如：

```csharp
case "request_user_input":
    Snapshot = Snapshot with
    {
        Status = CodexTaskStatus.NeedsInput,
        Activity = "等待你的输入",
    };
```

含义不是“猜测 Codex 想要帮助”，而是日志中明确出现了请求用户输入的事件，所以状态被设成 `NeedsInput`。

### 一个很细但很重要的处理

任务可能连续提出多个输入请求。代码会记录尚未得到工具输出的 `CallId`，而不是看到一次工具输出就武断地认为“已不需要你”。这避免了一个常见错误：有多个待确认问题时，状态过早从“等待你”变回“运行中”。

## 4. 为什么它不会每秒把整个历史再读一遍？

如果每秒重读一个很长的对话文件，会浪费磁盘和 CPU。为此项目使用了**增量读取**：

1. 记住上次读到的文件位置（类似书签）。
2. 下次只从书签后继续读取新增内容。
3. 只对新增事件运行状态机。

这由 [IncrementalJsonlReader.cs](../src/CodexTaskMonitor.Core/IncrementalJsonlReader.cs) 和 [CodexMonitorService.cs](../src/CodexTaskMonitor.Core/CodexMonitorService.cs) 配合完成。

可以把它类比成追小说：你不会每天从第一章重读，而是翻到昨天夹的书签继续读。

`CodexMonitorService` 是后端总协调者。它负责发现任务、监听文件变化、定期兜底扫描、调用状态机、把最新快照写入自己的 SQLite 缓存，并在状态变化时发布 `TaskEvent`。

## 5. 主窗口与桌宠为什么能自动更新？

这是典型的“模型和界面分开”的结构：

- **Core 层**：只处理数据、文件和状态，不认识按钮或窗口。
- **App 层**：处理 WPF 界面，不决定日志语义。
- **ViewModel**：介于两者之间，把任务数据翻译成界面容易直接显示的属性。

应用入口在 [App.xaml.cs](../src/CodexTaskMonitor.App/App.xaml.cs)。它创建 `CodexMonitorService`、主窗口和桌宠窗口，并用 `DispatcherTimer` 每秒让界面刷新一次：

```csharp
var tasks = _monitor.GetTasks();
_viewModel.Refresh(tasks);
_desktopPetViewModel?.Refresh(tasks);
```

`DispatcherTimer` 的好处是它运行在 WPF 的界面线程上，所以可以安全地更新屏幕控件。

另外，服务一发现状态改变会发事件，入口代码把 `_uiDirty` 设为 `true`，Toast 也能更快弹出。这相当于：有新消息时马上提醒，同时保留每秒校准，防止漏更新。

## 6. 桌宠如何决定显示哪几个任务？

对应文件是 [DesktopPetViewModel.cs](../src/CodexTaskMonitor.App/DesktopPetViewModel.cs)。它做了四件事。

### 6.1 先排除子代理

```csharp
var roots = tasks.Where(task => !task.IsSubagent).ToArray();
```

用户通常关心的是自己发起的主任务。子代理是主任务内部派生出来的工作单元，直接混在桌宠里会让列表显得重复，所以默认不显示。

### 6.2 统计摘要

它分别数出 `Running` 和需要关注的 `NeedsInput / Failed / Stale`，生成浮窗顶部的“几个需要你处理、几个进行中”。

### 6.3 排序，选出最该看的任务

排序优先级是：

```text
失败 > 等待输入 > 可能卡住 > 正在运行 > 已完成 > 已中止
```

同一级别再按最后活动时间由新到旧排序。这样即使很多任务同时运行，最需要你介入的一项也会成为浮窗主标题。

### 6.4 生成紧凑列表

```csharp
private const int VisibleTaskLimit = 4;
```

展开列表会在浮窗内部滚动，避免浮窗突然长成一个大面板。这样即使“最近完成”任务较多，也可以在浮窗内直接找到并打开对应对话。

`ObservableCollection<DesktopPetTaskItem>` 很适合绑定列表。它与普通 `List` 的区别在于：增删或替换元素时会主动告诉界面“我变了”，WPF 才知道该重画哪一行。

## 7. 点一下为什么能展开？

界面结构写在 [DesktopPetWindow.xaml](../src/CodexTaskMonitor.App/DesktopPetWindow.xaml)。XAML 是 WPF 描述界面的语言，作用有点像网页中的 HTML，但它直接用于 Windows 桌面程序。

顶部摘要是一个按钮，它绑定到：

```xml
Command="{Binding ToggleExpandedCommand}"
```

这里的 `Command` 是“命令”，不是把点击逻辑塞进 XAML 里。真正逻辑在 ViewModel：

```csharp
[RelayCommand]
private void ToggleExpanded() => IsExpanded = !IsExpanded;
```

`[RelayCommand]` 是 CommunityToolkit.Mvvm 帮我们自动生成 `ToggleExpandedCommand` 的标记。`IsExpanded = !IsExpanded` 意思是把 `true/false` 翻转，也就是“展开变收起，收起变展开”。

窗口的 [DesktopPetWindow.xaml.cs](../src/CodexTaskMonitor.App/DesktopPetWindow.xaml.cs) 会根据 `IsExpanded`：

1. 显示或隐藏任务列表区域。
2. 根据当前任务数计算窗口高度。
3. 把窗口高度限制在 410，避免列表异常时占满屏幕。

这是一个很实际的 UI 原则：**数据量可以变化，但界面边界必须受控**。

## 8. 点击任务为什么能回到正确的 Codex 对话？

每个任务都有 `ThreadId` 和 `DeepLink`。当展开列表的一行被点击，XAML 把那一行对应的 `DesktopPetTaskItem` 作为参数交给 `OpenTaskCommand`：

```xml
CommandParameter="{Binding}"
```

再由 ViewModel 调用应用入口在创建它时传入的回调：

```csharp
_openTask(task.Snapshot);
```

最后 [DeepLinkLauncher.cs](../src/CodexTaskMonitor.App/DeepLinkLauncher.cs) 使用任务中的 `codex://threads/...` 深链，让 Windows 将链接交给 Codex Desktop。

这里值得注意的是：桌宠没有自己保存“要打开第几行”。它携带的是任务对象本身，所以无论排序如何变化，点击都对应那个任务的唯一 `ThreadId`，不会误跳到别的会话。

## 9. 你可以怎样阅读源码？

推荐不要从最底层开始硬读。按这个顺序，每次只追一个问题：

1. 先看 [App.xaml.cs](../src/CodexTaskMonitor.App/App.xaml.cs)：程序启动时创建了哪些对象？
2. 再看 [DesktopPetViewModel.cs](../src/CodexTaskMonitor.App/DesktopPetViewModel.cs)：数据如何变成摘要和任务行？
3. 看 [DesktopPetWindow.xaml](../src/CodexTaskMonitor.App/DesktopPetWindow.xaml)：这些属性具体在哪儿显示？
4. 追到 [CodexMonitorService.cs](../src/CodexTaskMonitor.Core/CodexMonitorService.cs)：任务列表怎么产生？
5. 最后看 [TaskStateMachine.cs](../src/CodexTaskMonitor.Core/TaskStateMachine.cs)：每一种日志事件如何改变状态？

读代码时很有用的技巧是“沿着一个变量走”。例如从 `ActiveTasks` 开始：

```text
Refresh(tasks)
  -> UpdateActiveTasks(activeTasks)
  -> ActiveTasks.Add(...)
  -> XAML ItemsControl ItemsSource="{Binding ActiveTasks}"
  -> 屏幕出现一行可点击任务
```

你不需要先理解所有类，只要能讲清这个变量从哪里来、经过什么转换、最后显示在哪儿，就已经真正读懂了一条功能链路。

## 10. 这套设计中最值得带走的工程观念

1. **先分层**：数据监控与界面分开，之后换桌宠皮肤、增加网页面板，都不用重写任务判断。
2. **用明确事件，不猜屏幕**：读取结构化日志比 OCR 或截图稳定得多。
3. **用状态机减少混乱**：把“任务现在是什么状态”集中在一个地方定义。
4. **增量处理**：记录读取位置，只处理新内容，程序才能长期后台运行。
5. **保留降级方案**：数据库读不到时仍能扫 JSONL，工具不容易因为一个外部变化整体失效。
6. **界面优先展示行动信息**：失败、等待输入、可能卡住比“正在运行”更靠前，因为它们更需要你做决定。

## 小练习（不需要改项目）

打开桌宠，观察一个任务从“正在运行”到“等待你的输入”或“完成”时，尝试用上面的状态图解释它为什么改变。接着在源码里搜索 `request_user_input` 或 `task_complete`，你会看到这个视觉变化从日志事件到窗口文字的完整因果链。
