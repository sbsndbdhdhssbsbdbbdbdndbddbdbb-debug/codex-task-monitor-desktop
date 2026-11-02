# Codex Task Monitor User Guide

[Project Home](../README.en.md) | [简体中文](USER_GUIDE.zh-CN.md) | [Custom Pets](CUSTOM_PETS.md)

This is a standalone Windows x64 status monitor, not a Codex task runner. Start work in Codex, then track it here. Replying, granting permission, resuming, stopping, and verifying results still happen in Codex. The current UI is primarily Chinese; the labels below match that UI.

This guide covers **`v0.1.0-beta.1`, a preview, not a stable release**. See the [Release list](https://github.com/sbsndbdhdhssbsbdbbdbdndbddbdbb-debug/codex-task-monitor-desktop/releases) for version notes and available assets, and [Security](../SECURITY.md) for maintenance scope.

## Quick Workflow

| Goal | Action | Result And Check |
| --- | --- | --- |
| Start monitoring | Extract and run `CodexTaskMonitor.exe`; start tasks normally in Codex | Wait for the first scan; if empty, check the selected group, filters, and pause state |
| Check multiple chats | Click the pet and switch between 进行中 / 需处理 / 最近完成 | Status inferred from local records, not a percentage |
| Return to a chat | Click a floating-list task; double-click a main-window task or use its open button | Attempts navigation; find the chat manually in Codex if it fails |
| Inspect details | Select a task in the main window | See activity, elapsed time, timestamps, directory, and detected children |
| Work quietly | Enable 勿扰 (Do Not Disturb) | Monitoring continues while notifications are suppressed; monitoring pause does not pause Codex |
| Recall the pet | Open the task center from the tray and choose 显示桌宠 (Show pet) | Recalls a hidden/collapsed pet; the tray also has a display toggle |
| Change companions | Choose an existing pet in 桌宠库 or download from 发现桌宠 | A successful download applies immediately; the library stays open |

## Download, Extract, And Start

1. Visit the [Release list](https://github.com/sbsndbdhdhssbsbdbbdbdndbddbdbb-debug/codex-task-monitor-desktop/releases), review `v0.1.0-beta.1` notes and available assets, and choose `CodexTaskMonitor-win-x64.zip`. Do not substitute an unknown mirror.
2. Extract the entire ZIP to a writable folder such as `<app-folder>`. This is not an installer; do not run it inside the archive.
3. Launch `CodexTaskMonitor.exe`. The self-contained release package includes the .NET 8 runtime; no separate .NET Desktop Runtime installation is needed.
4. Use Codex normally. The monitor currently looks under the current Windows user's `%USERPROFILE%\.codex`; there is no UI for choosing an arbitrary Codex data root.

Retain both READMEs, the bilingual guides and custom-pet manual in `docs`, `LICENSE`, third-party/security notices, and dependency/runtime license material under `licenses`. Running the extracted EXE does not require an SDK or PowerShell 7. Source publishing requires Windows, the latest supported .NET 8 SDK, and PowerShell 7. This preview's build baseline is SDK `8.0.427` / runtime `8.0.33`; see [Project Home](../README.en.md). Its default output directory, `artifacts/publish/win-x64`, must be empty; use `-OutputDirectory` for another empty build folder.

.NET 8's [official support end date is 2026-11-10](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Bundling the runtime does not extend its lifecycle. Continued security maintenance after that date requires a framework migration; see [Security](../SECURITY.md). Do not treat this preview package as a long-term supported release.

The application is unsigned, and Windows may show a security warning. Verify the download source, release information, and your organization's requirements. Stop if trust cannot be established. Do not disable protections or bypass security policy to run it.

Closing the main window **hides it to the system tray; it does not exit**. Right-click the Codex Task Monitor tray icon and choose **退出** (Exit) to stop the program. If a second launch says it is already running, locate the existing tray instance; relaunching does not guarantee window recall.

## Understanding Status

| UI Group | Current Signal | Does Not Establish |
| --- | --- | --- |
| 进行中 / In progress | Session events such as task start, reasoning, tool calls, or output | Remaining time or completion percentage |
| 需处理 / Needs attention | Waiting for user input or a detected failure | Coverage of every approval, permission prompt, or error |
| 最近完成 / Recently completed | Completion records within the last 24 hours | Tested, submitted, or user-accepted results |

The three groups count root tasks; detected children with a known parent appear in the inspector. Not every internal status is displayed: idle, aborted, and `Stale` states are outside these groups. By default, running tasks become stale after 10 minutes without new activity. An absent task is not necessarily complete, and a long tool call may look stale.

Discovery has a recent-record limit and depends on Codex's local database schema and JSONL format. It cannot guarantee complete history, every chat, every input-wait condition, or compatibility with future Codex versions. Treat Codex as the source of truth.

Clicking a completed item in the floating recent list acknowledges that completion and removes it from that list **before the navigation attempt, even if navigation then fails**. It remains available in the main window's recent group. This does not delete the Codex chat.

## Main Window

- **Find:** Search task titles, project names, or current activity and narrow by project. The clear-filter button resets search and project scope, not stored data.
- **Sort:** 最近活动 (Latest activity) sorts newest first; 运行时长 (Elapsed time) sorts by duration. These times do not predict work remaining.
- **Inspect:** Select a task to see its recorded title, status, project, activity, elapsed time, last activity, start time, working directory, and detected children. Titles may be truncated; they are not necessarily the complete original message.
- **Open:** Double-click a row, press Enter on the selection, use its arrow, or choose 在 Codex 中打开 (Open in Codex). Child-task buttons also attempt navigation. Esc or the inspector close button clears selection.
- **Recent shortcuts:** With no selection, 刚刚完成的事 shows up to three recent tasks. Choosing one locates its record in the main recent group.
- **Check monitoring:** Refresh performs a manual read. 监控状态 (Monitor status) reports tracking count and the last check, not whether task results are correct.

Navigation uses `codex://threads/<thread-id>` and local application handling. Installation, protocol registration, or version changes can result in merely bringing the app forward, opening the wrong location, or failing entirely. Deep-link success is not guaranteed, and the monitor does not enter tasks for you.

## Pet, Orb, And Tray

| Action | Current Behavior |
| --- | --- |
| Click the pet body | Expand/collapse its task list; switch groups, scroll, and click tasks there |
| Choose 收纳为小圆球 (Collapse to orb) | Show an approximately 48 x 48 orb; click it to restore the pet, then click the pet to expand its list |
| Hold and drag the pet or orb | Move between monitors; release constrains it to the current monitor's work area, and the orb snaps to the nearest edge |
| Choose 显示桌宠 (Show pet) in the main window | Display the pet and restore it from orb mode without restarting |
| Left/right-click the tray icon | Left-click opens the task center; right-click offers task center, pause/resume, DND, display toggle, and exit |

The orb prioritizes the attention count, otherwise showing the running count. It is **not a total chat count or progress percentage**. Settings control floating-window visibility, topmost display, and animation. System animation preferences can also disable animation.

After display or scaling changes, recall the pet from the task center; exit and restart if necessary. Multi-monitor dragging is implemented, but identical behavior across all DPI, taskbar, and monitor combinations is not guaranteed.

## Notifications, DND, And Pause

In 设置 (Settings), 普通完成提醒 (Normal completion notifications) offers:

| Option | Normal Completion Behavior |
| --- | --- |
| 始终提醒 / Always | Notify for eligible detected completion events |
| 仅在窗口后台时提醒 / Background only | Default; checks whether the monitor's main window is active, not whether Codex is in the foreground |
| 只提醒需处理和失败 / Attention and failures only | No normal completion notifications |
| 关闭普通完成提醒 / Disable normal completion notifications | Disables normal completion alerts, not every exceptional alert |

Input-wait, failure, and abort events have separate notification logic. Child-task completions and stale states do not trigger normal completion alerts. Nearby updates may be aggregated in an app popup: one update attempts to open its chat; multiple updates open the task center. Loading old records at startup does not guarantee replay of completion alerts.

播放提醒声音 (Play notification sound) controls sound. 需处理提醒间隔 (Attention reminder interval) accepts 5 to 240 minutes, default 30, for tasks still waiting for input. It is not a retry schedule for every failure.

| Control | Monitoring Effect | Codex Task Effect |
| --- | --- | --- |
| 勿扰 / Do Not Disturb | Keep monitoring while suppressing popups and sound; available in main window, floating list, and tray | None; does not reply or resume a task |
| 暂停监听 / Pause monitoring | Suspend automatic background reads and retain the last state; manual refresh can still read | None; Codex may keep working |
| Exit monitor | Stop this tool's monitoring and windows | Does not stop Codex |

Pause is not a mute switch; use DND for quiet work. Paused lists are not current, and an elapsed-time display does not prove a task is still working.

登录 Windows 后自动启动 registers the current user's startup entry. 启动后直接进入托盘 controls main-window visibility on startup. Before moving or deleting the app, disable automatic startup in settings and save.

## Petdex Library

1. Open 桌宠库 (Pet library). 我的桌宠 (My pets) contains built-ins and readable local packages. Click a card to switch immediately without downloading it again.
2. 发现桌宠 (Discover) loads the [Petdex](https://petdex.dev/) catalog. Search names, descriptions, kinds, or authors and use previous/next page; there are 12 entries per page. Thumbnails do not verify compatibility with every action.
3. Choose 下载并使用 (Download and use). A successful download applies the pet and returns to My pets. Alternatively, paste a real catalog slug, `https://petdex.dev/pets/<slug>`, or `npx petdex install <slug>` into 添加新伙伴 (Add companion).
4. The command text is **parsed for its slug, not executed**. Node.js, npx, and Petdex Desktop are not needed. Arbitrary download URLs, local paths, and ZIPs are not supported imports in that field.
5. Cancel loading, retry failures, or keep using a built-in/downloaded pet. Click a failed thumbnail to retry it. 官方浏览 opens Petdex in your default browser; it does not perform the in-app download for you.
6. In My pets, use a downloaded pet's delete icon and confirm to remove its entire local package. Deleting the active downloaded pet first switches to a built-in. Built-ins have no delete action. Refresh rescans packages in My pets and reloads the online catalog in Discover.

There is no one-click arbitrary folder, ZIP, or single-image import. Self-designed art needs the developer/manual [package format](CUSTOM_PETS.md). The current online adapter assumes 8 columns x 9 rows; compatibility with all Petdex sprite versions, actions, or layouts is not guaranteed.

Keeping source metadata is not a license review. Retain author and license information and separately check use, modification, display, and redistribution permissions. Do not label every third-party character MIT or treat non-commercial use as permission.

## Storage And Privacy Limits

| Location | Data |
| --- | --- |
| `%USERPROFILE%\.codex` | Reads session JSONL, state databases, and error-log databases; not a monitor uninstall directory |
| `%LOCALAPPDATA%\CodexTaskMonitor\settings.json` | Notification, pet, startup, and API preferences |
| `%LOCALAPPDATA%\CodexTaskMonitor\monitor.db` | Task snapshots, read cursors, notification deduplication; may contain titles, projects, paths, activity, and error information |
| `%LOCALAPPDATA%\CodexTaskMonitor\logs` | Monitor diagnostics; inspect before sharing |
| `%LOCALAPPDATA%\CodexTaskMonitor\integration.json` and `state.json` | API connection/token information and state snapshots; disabling the API does not erase existing state files |
| `<project-root>\Pets\<id>` or `<app-folder>\Pets\<id>` | Search upward from the executable directory for the nearest `.sln` directory and use its `Pets`; otherwise use `Pets` beside the app, not the terminal's current directory |

Even if extracted to another drive, user data and .NET temporary extraction caches may use the system drive. The app cannot promise to avoid the C drive.

No remote task-content upload path was found in the current code, but monitoring parses sessions and may derive titles from user-message excerpts. Petdex catalog, thumbnail, and download requests contact third-party servers and catalog-referenced resource hosts, normally exposing IP addresses and request targets. Browser visits also follow the browser's and website's own privacy behavior. This is not an entirely offline tool.

Privacy mode defaults to on but is not global anonymization:

| Surface | Current Masking And Remaining Data |
| --- | --- |
| Notifications | Replace title and activity; a single-task popup can still show the project name |
| HTTP / WebSocket API | Replace title and activity; project, thread/parent IDs, deep links, timestamps, and some agent metadata remain |
| `state.json` | Replace title/activity and clear working-directory and session-file paths; retain project, identifiers, and other fields |
| Database, diagnostics, main window, floating list, screenshots | Not anonymized by this setting; not suitable for direct public sharing |

After changing privacy mode, exit and reopen the monitor so the API and state file use the new startup settings. This does not retroactively clean databases, logs, file copies, or screenshots. This repository's public test screenshots use synthetic tasks, placeholder paths, and the original built-in mint pet (薄荷终端), not personally downloaded pets, real chats, or private directories.

## Local API

启用桌宠 API (Enable pet API) defaults to on at port `47831`, bound only to `127.0.0.1`. Exit and restart after changing the port or enabled state. Ports must be available values from 1024 through 65535.

| Endpoint | Purpose | Authentication |
| --- | --- | --- |
| `GET /api/v1/health` | Health, pause state, and task count | No token |
| `GET /api/v1/tasks` | Current task snapshots | `Authorization: Bearer <token>` |
| `GET /api/v1/tasks/<thread-id>` | One task | Same |
| `WS /api/v1/events` | Initial snapshot and subsequent events | Same header during handshake |

Read addresses and the token generated at each API start from `%LOCALAPPDATA%\CodexTaskMonitor\integration.json`. Do not share the file or forward this port to the internet. Loopback binding and a token do not protect against programs that can read your local files. Disable the API and restart when integration is not needed. It has no endpoint for executing Codex tasks.

## Troubleshooting

| Symptom | First Checks |
| --- | --- |
| A task is missing | Clear search/project filters; check groups, pause, and the default Codex data location; refresh or open monitor status. Discovery and format support are limited; do not delete Codex data to reset it |
| Status differs from Codex | Trust Codex; resume/restart the monitor and report a sanitized event format and reproduction steps |
| No completion popup | Check DND, the normal-completion option, and whether the monitor window is active. Old records, children, and aggregated updates may not alert separately |
| Clicking does not locate the chat | Find it manually in Codex; check installation/protocol association. Starting the app is not proof of successful chat navigation |
| Petdex load/download fails | Retry/cancel, check connectivity/service availability, and use built-ins meanwhile. Do not run unknown installation commands |
| Custom pet missing or misaligned | Check dedicated metadata, filenames, atlas, and action order in Custom Pets |
| Local API connection fails | Check enable/restart, port conflicts, and the current token. API failure does not necessarily mean monitoring has stopped |

## Update And Remove

Exit through the tray before updating. Extract the new package into a separate directory, verify its version, then replace the app. If moving it, check the actual pet location and automatic-startup setting. Pets may remain in the old application or source directory; settings/database remain in this tool's LocalAppData directory.

To remove, disable automatic startup and save, exit through the tray, then delete the app folder you extracted. To clear this tool's records, separately remove `%LOCALAPPDATA%\CodexTaskMonitor` after exit, once you have decided what settings/history to retain. Back up self-designed pets and handle leftover `Pets` separately. Do not delete unrelated files from a shared source checkout.

**Do not delete `%USERPROFILE%\.codex`, Codex chats, work projects, or other personal Codex folders to uninstall this monitor.** Its own snapshots and Codex's sessions are separate data.

## Project Files

[LICENSE](../LICENSE) | [Third-party notices](../THIRD_PARTY_NOTICES.md) | [Security policy](../SECURITY.md) | [Contributing](../CONTRIBUTING.md) | [Code of conduct](../CODE_OF_CONDUCT.md)

These security and community files are provided in the source repository; the extracted package does not contain every community file. The project-code license does not automatically cover third-party pets, character designs, or dependencies, and does not establish that every license has been verified.
