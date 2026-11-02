# v0.1.0-beta.1 / 首个公开预览版

## 中文

面向 Windows x64 的独立 Codex 会话监视器。起因是多条聊天在后台同时运行时，希望用一个独立的小窗口分清“进行中、需处理、最近完成”，收到提醒，并点回对应聊天。不是 OpenAI 或 Petdex 官方产品。

- 任务中心支持搜索、项目筛选、排序、活动与子任务详情。
- 可拖动的悬浮桌宠、小圆球、跨显示器移动、勿扰与托盘召回。
- Petdex 在线目录预览、搜索、按需下载并应用；个人下载素材不随本发布包分发。
- 提供中英文使用手册、自制桌宠手工包说明、社区准则、安全与第三方声明。
- 已替换旧图片处理依赖，增加 PNG/WebP 类型、编码大小和解码像素限制，并更新数据库及测试依赖。

### 下载

下载 `CodexTaskMonitor-win-x64.zip`，完整解压后运行 `CodexTaskMonitor.exe`。无需另装 .NET。核对 `SHA256SUMS.txt` 和来源，不要关闭系统防护。程序未签名，Windows 可能显示安全提示。

### 验证与限制

公开前通过 94 项单元测试、44 项界面流程检查，以及发布包拦截规则检查。准备时的 NuGet 审计未报告已知依赖漏洞；这不等于没有未知问题。公开截图使用合成任务与内置原创桌宠，不包含真实聊天。

这是预览版：状态从本地记录推断，可能延迟或误判；跳转依赖本机 Codex 协议；不显示没有真实来源的进度百分比。任意单图/ZIP 的一键导入尚未实现。第三方素材授权独立于项目代码许可，免费和开源不是素材免责保证。

本版包含 .NET 8.0.33；.NET 8 官方支持于 2026-11-10 结束，之后继续安全维护需要迁移到受支持的框架。本版不承诺长期支持。详情见 `SECURITY.md`。

## English

An independent Windows x64 monitor for local Codex chats. It grew out of a multi-chat background workflow: a separate glanceable view of In progress, Needs attention and Recently completed, completion notifications, and a way to jump back to the corresponding chat. Not an official OpenAI or Petdex product.

- Searchable/filterable/sortable task center with activity and child-task details.
- Draggable floating pet and compact orb, multi-monitor movement, Do Not Disturb and tray recall.
- Petdex catalog previews/search and on-demand download/apply. Personally downloaded artwork is not bundled.
- Chinese/English guides, manual custom-pet package instructions, community/security policies and third-party notices.
- Replaced the old image dependency, added PNG/WebP and decoded-image limits, and refreshed database/test dependencies.

Download `CodexTaskMonitor-win-x64.zip`, extract everything, and run `CodexTaskMonitor.exe`. No separate .NET installation is required. Check the source and `SHA256SUMS.txt`; keep operating-system protections enabled. The executable is unsigned and may trigger a security warning.

Pre-publication checks passed 94 unit tests, 44 UI workflow assertions and release-content guard tests. The preparation-time NuGet audit reported no known dependency vulnerabilities, not a guarantee against unknown flaws. Published screenshots use synthetic tasks and original built-in artwork.

This is a preview. State inference may lag or be wrong; chat navigation depends on local Codex protocol handling. There is no invented progress percentage or one-click arbitrary image/ZIP import. Third-party artwork permissions are separate from the code license; free/open-source distribution is not blanket copyright clearance.

Bundled runtime: .NET 8.0.33. Microsoft's .NET 8 support ends on 2026-11-10; continued security maintenance after that requires migration to a supported framework. No long-term support commitment is made for this preview. See `SECURITY.md`.
