# 自制桌宠 / Custom Pets

[中文首页](../README.md) | [English Home](../README.en.md) | [中文手册](USER_GUIDE.zh-CN.md) | [English Guide](USER_GUIDE.en.md) | [Releases](https://github.com/sbsndbdhdhssbsbdbbdbdndbddbdbb-debug/codex-task-monitor-desktop/releases)

适用于预览版本 `v0.1.0-beta.1`，不是稳定的通用宠物格式承诺。 Applies to preview `v0.1.0-beta.1`, not a stable, universal pet-format contract.

## 中文说明

### 先区分两条路径

| 路径 | 当前支持 | 边界 |
| --- | --- | --- |
| 应用内 Petdex 目录 | 预览、搜索、下载并应用、复用、删除下载包；粘贴 slug、Petdex 页面 URL 或 npx 安装文本 | 安装文本只解析，不执行；通过目录匹配下载，不是通用 URL 导入 |
| 自己设计的桌宠 | 开发者手工准备符合当前读取器的专用包，放入实际 `Pets` 后刷新 | 没有任意本地文件夹、ZIP 或单张图片的一键导入；不是图像转动画工具 |

单张图片不会自动变成多个动作，ZIP 不会被自动解包，任意 `pet.json` 文件夹也不会被当成可用桌宠。本说明记录的是当前监视器的手工适配约定，不是对所有 Petdex/Codex 宠物格式的兼容声明。

### 找到实际存储目录

程序从自身所在目录开始向上寻找最近的、包含 `*.sln` 的目录：找到时使用 `<project-root>\Pets`；找不到时使用 `<app-folder>\Pets`。这不是终端当前目录，也不是固定某个盘符。

例如，独立解压版通常放在 `<app-folder>\Pets\paper-helper`；从源码目录下的发布输出运行时，可能使用 `<project-root>\Pets\paper-helper`。设置和任务数据库仍在 `%LOCALAPPDATA%\CodexTaskMonitor`，不能据此承诺不使用 C 盘。

先备份自己的图像，选一个不会覆盖内置或已下载角色的唯一 ID。示例使用虚构原创角色 `paper-helper`，没有附带或引用任何下载角色图像。

### 包文件

按下面的完整包组织；目录名与元数据 ID 保持一致，使用小写英文字母、数字、连字符或下划线：

```text
<pets-root>/
  paper-helper/
    petdex-package.json
    pet.json
    spritesheet.png
    AUTHOR.md
    LICENSE.txt
```

`AUTHOR.md` 和 `LICENSE.txt` 是供人查阅的署名/授权文件，不会被当前渲染器解析。保留实际作者、原始来源、修改说明和真实许可证，不要把示例当成授权证明。

**识别关键是 `petdex-package.json` 和可读取的图集。** 当前 `PetLibraryStore.ListAsync` 扫描该专用元数据和固定名图集；不会从任意 `pet.json` 自动发现包。在线安装器会生成上面的三项主要文件；手工包也按此结构准备。显示名、布局和动作映射以专用元数据为准，单独修改 `pet.json` 不会改变这些设置。

### 图集与动作

使用透明背景 PNG，分成 **8 列 x 9 行**等大的格子；每行代表一个动作，8 列是依次循环的帧。建议宽度能被 8 整除、高度能被 9 整除。例如 `1024 x 1152` 得到 `128 x 128` 的单帧。这只是尺寸示例，不是已经提供的图像文件。

动作顺序由 `actions` 数组确定，数组第 0 项就是图集第 0 行。名称匹配不区分大小写，建议统一小写、不重复。下方六项动作示例只命名了前六行；第 6 至 8 行仍应存在，可放重复的空闲帧，不会自动获得新行为。

| 监视器状态 | 按优先级查找动作名 | 找不到时 |
| --- | --- | --- |
| 无可映射状态 / 空闲 | 固定第 0 行，建议将 `idle` 放在此行 | 不按动作名称另找行 |
| 进行中 | `running`、`run`、`run-right`、`runright` | `idle` 行，缺失则第 0 行 |
| 需处理 | `waiting`、`failed`、`review` | 同上 |
| 最近完成 | `waving`、`wave`、`review` | 同上 |

当前“需处理”的失败和等待输入使用同一显示类别的动作，不分别播放两个动作。额外的动作名不会自动增加交互；没有根据任意 `pet.json` 定义播放跳跃、睡眠、拖拽等动作的通用引擎。

下方示例的实际行含义：0=`idle`，1=`run`，2=`failed`，3=`review`，4=`wave`，5=`waiting`，6 至 8 未命名。因此进行中用第 1 行，需处理优先用第 5 行，完成用第 4 行；第 2、3 行是去掉更优先名称后才可能使用的回退，不表示会随机播放全部动作。

注意当前实现的限制：

- 在线下载适配固定为 8 x 9，在线缩略图也按 8 x 9 取左上第一帧。不能假设 Petdex 中标注的每种版本或布局都兼容。
- 本地元数据虽然接受 1 至 32 的行列数，数字通过校验不等于布局适配正确。内置角色的 8 x 6 是另一套资源约定，不能直接据此推断下载包布局。
- 播放器将每行全部列作为循环帧，使用自身固定节奏，不解释任意逐动作帧数、独立帧时长或复杂动画描述。
- 当前播放器会内缩裁切单帧：水平每侧 `min(18, frameWidth / 4)` 像素，垂直每侧 `min(14, frameHeight / 6)` 像素，按整数除法计算。在 `128 x 128` 帧中每侧分别裁掉 18 和 14 像素，请给主体留透明边距并检查是否被裁。
- 下载图集限制为 12 MiB，安装时把可读取的 PNG/WebP 规范化为 PNG；本地扫描也有图像格式和大小检查。手工制作建议使用有效的 `spritesheet.png`，不是通过改扩展名伪装图片。
- 下载/预览解码当前还限制每边不超过 8192 像素、总像素不超过 16,777,216；这些是拒绝过大资源的限制，不是对任何上限内图集的兼容保证。

## 元数据示例 / Metadata Examples

下面两个 JSON 是完整的字段示例，不包含任何图片、真实作者身份或许可证授予。`example.invalid` 是不可用的占位域名；请替换为你可公开的真实来源/授权说明地址，不要填个人机器路径或邮箱。

These are complete field examples, not supplied artwork, an author identity, or a license grant. `example.invalid` is a non-working placeholder domain. Replace it with appropriate public source/permission information, not personal machine paths or emails.

### `petdex-package.json`

```json
{
  "id": "paper-helper",
  "name": "Paper Helper",
  "description": "An original paper-shaped task companion.",
  "sourcePageUrl": "https://example.invalid/pets/paper-helper",
  "packageUrl": "https://example.invalid/pets/paper-helper/pet.json",
  "spriteSheetUrl": "https://example.invalid/pets/paper-helper/spritesheet.png",
  "actions": ["idle", "run", "failed", "review", "wave", "waiting"],
  "layout": {
    "columns": 8,
    "rows": 9
  },
  "originalPackageJson": null
}
```

### `pet.json`

```json
{
  "id": "paper-helper",
  "displayName": "Paper Helper",
  "description": "An original paper-shaped task companion.",
  "spritesheetPath": "spritesheet.png"
}
```

`sourcePageUrl`、`packageUrl` 和 `spriteSheetUrl` 记录来源，不是本地读取时自动联网获取缺失图像的指令。`originalPackageJson` 在下载包中可保留原始 JSON 文本；手工包可为 `null`，不是再嵌套一份对象。

These URL fields record provenance; local loading does not use them to fetch a missing sheet. `originalPackageJson` can retain the original downloaded JSON as a string; use `null` for this manual example, not a nested JSON object.

### 手工使用与排查

1. 退出监视器，将准备好的完整文件夹放到实际 `<pets-root>`，避免同 ID 覆盖，也不要放入个人 Codex 会话文件夹。
2. 启动后打开“桌宠库” -> “我的桌宠” -> 刷新；点击卡片应用。也可在已有库窗口刷新后选择。这里只是扫描手工放置的专用包，没有打开文件选择器进行导入。
3. 检查静止帧、透明边缘、动画循环和各个状态映射。包测试使用合成任务和占位路径，不要为了截图暴露真实聊天。本仓库公开的界面测试截图统一使用原创内置“薄荷终端”（`mint`），不使用个人下载桌宠；这不限制你在本地检查自己制作的素材。
4. 不出现时检查文件名、有效 JSON、非空 ID/名称、`layout`、图像是否可解码及大小；动作错位时检查 `actions` 顺序与实际行。错误文件可能被跳过，不代表每种错误都有详细提示。
5. 当前 UI 将非空 `sourcePageUrl` 的包标成“Petdex”并显示删除按钮，即使它是手工制作的原创包。这是现有来源标签逻辑，不是 Petdex 收录或授权认证。不要为获得标签而虚构来源。
6. 删除按钮会移除**整个包目录**，包括放在其中的作者和许可证文件；自制原稿请另存备份。删除活动包会先切回内置角色。手工移除前也建议先切换到内置角色并退出应用。

### 作者与许可证

只使用自己创作或获得相应权限的图像。保留作者、来源、许可证文本及修改记录；如有底层角色/品牌权利，另行核对展示、修改和分发许可。素材可下载、标成开源或用于非商业场景，均不能由本项目替你确认所有权利。

下载器保留部分来源元数据和原始包 JSON，但不保证自动保存全部作者、署名和授权材料。请自行保留，不要把项目代码的 MIT 许可套到第三方素材，也不要把未经许可的角色随应用、公开截图或仓库重新分发。

## English Notes

### Supported Routes

The in-app Petdex library supports catalog previews, search, download/apply, reuse, and package deletion. Its input accepts a catalog slug, a Petdex pet-page URL, or npx installation text. The text is parsed, not executed; it is not an arbitrary-URL installer.

Self-designed pets use a **developer/manual package**, not a one-click arbitrary folder, ZIP, or image importer. A single picture is not automatically animated, ZIPs are not unpacked, and a folder containing only `pet.json` is not discovered as a pet. This is the current monitor adapter's contract, not universal Petdex/Codex format support.

### Location And Files

Starting at the executable directory, the app searches upward for the nearest directory containing `*.sln` and uses `<project-root>\Pets` there. Without one, it uses `<app-folder>\Pets` beside the executable. The terminal's working directory does not select storage. Settings and task data still use `%LOCALAPPDATA%\CodexTaskMonitor`; this does not imply avoiding the C drive.

Back up your original art. Use a unique lowercase ASCII ID with letters, digits, hyphens, or underscores, avoiding built-in/existing IDs, and match the folder name to it. The fictional `paper-helper` structure and the two JSON examples above form the complete manual example. No downloaded character art is supplied.

Prepare `petdex-package.json`, `pet.json`, and `spritesheet.png`. `AUTHOR.md` and `LICENSE.txt` are human-readable provenance/permission files, not renderer configuration. Preserve real authors, sources, modifications, and licenses rather than treating the example as a permission grant.

Discovery is driven by **`petdex-package.json` plus a readable fixed-name sheet**. The current `PetLibraryStore.ListAsync` does not discover arbitrary `pet.json` folders; the online installer generates the three main files, and manual packages should follow that structure. Display name, layout, and actions come from the dedicated metadata. Editing only `pet.json` will not change them.

### Atlas And Action Mapping

Use a transparent PNG with **8 columns x 9 rows** of equal cells. Each row is an action; columns are successive looping frames. Use dimensions divisible by 8 and 9, respectively. For example, `1024 x 1152` produces `128 x 128` frames; this is a size example, not a supplied file.

`actions[0]` names row 0, and so on. Matching is case-insensitive; lowercase, unique names are recommended. The six-action JSON example names only rows 0 through 5. Rows 6 through 8 must still exist and can repeat idle art; they do not gain new behavior automatically.

| Monitor State | Action Names In Priority Order | Fallback |
| --- | --- | --- |
| Idle / unmapped state | Fixed row 0; put `idle` here | Not resolved by action name |
| In progress | `running`, `run`, `run-right`, `runright` | Idle row, otherwise row 0 |
| Needs attention | `waiting`, `failed`, `review` | Same |
| Recently completed | `waving`, `wave`, `review` | Same |

Failures and input waits share the attention category's animation; they do not independently select failure/wait animations. Extra names do not create interactions. There is no generic engine for arbitrary jump, sleep, drag, or other behavior definitions from `pet.json`.

In the example, rows are 0=`idle`, 1=`run`, 2=`failed`, 3=`review`, 4=`wave`, 5=`waiting`, then three unnamed rows. Running uses row 1, attention prefers row 5, and completion uses row 4. Rows 2 and 3 are lower-priority fallbacks if the preferred labels are removed, not a random animation playlist.

Current constraints:

- Online download adaptation and thumbnail extraction assume 8 x 9; thumbnails use the first top-left frame. Not every Petdex sprite version/layout is compatible.
- Local layout metadata accepts dimensions from 1 to 32, but numeric acceptance does not prove compatibility. Built-in 8 x 6 art follows a separate convention.
- The player loops every column with its own fixed cadence. It does not interpret arbitrary per-action frame counts, per-frame durations, or complex animation schemas.
- Each frame is inset-cropped by `min(18, frameWidth / 4)` pixels on each horizontal side and `min(14, frameHeight / 6)` on each vertical side, using integer division. A `128 x 128` frame loses 18 pixels per horizontal side and 14 per vertical side. Leave transparent margins and inspect for clipping.
- Downloads have a 12 MiB sheet limit and normalize readable PNG/WebP into PNG; local scanning also checks format and size. Use a valid `spritesheet.png` for manual packages, not a renamed unrelated file.
- Download/preview decoding also caps each dimension at 8192 pixels and total area at 16,777,216 pixels. These reject oversized resources; they do not establish compatibility for every atlas below the limits.

### Apply And Troubleshoot

1. Exit the monitor and place the complete folder under the resolved `<pets-root>`, not your personal Codex session directory. Avoid overwriting an existing ID.
2. Open 桌宠库 -> 我的桌宠 -> refresh and choose its card. An already-open library can also rescan. This scans manually placed dedicated packages; it is not a file-picker import workflow.
3. Inspect the still frame, transparency, loop, and state mappings. Test packages with synthetic tasks and placeholder paths, not real chats. This repository's public UI test screenshots use the original built-in mint pet (薄荷终端), not personally downloaded pets; you can still inspect your own artwork locally.
4. If missing, check exact filenames, valid JSON, nonempty ID/name, layout, readable image, and size. For wrong actions, compare array order with actual rows. Invalid packages may be skipped without a detailed error for every case.
5. The current UI labels packages with a nonempty `sourcePageUrl` as Petdex and offers deletion, even for manually authored originals. This is a labeling rule, not Petdex listing, endorsement, or license verification. Do not fabricate provenance to obtain a label.
6. Deletion removes the **entire package directory**, including author/license files inside it. Keep original art elsewhere. Removing the active package switches to a built-in first; switch to a built-in and exit before manual removal too.

### Attribution And Permissions

Use your own art or art for which you have the necessary permission. Keep author, source, license text, and modification records. Check underlying character/brand rights separately for display, modification, and distribution. Availability for download, an open-source label, or non-commercial use does not establish all permissions.

The downloader keeps some provenance fields and original package JSON, but does not guarantee collecting all attribution or license material. Retain those separately. This project's MIT code license does not relicense third-party pets. Do not redistribute unauthorized characters in the app, public screenshots, or repository.

## 相关文件 / Related Files

[LICENSE](../LICENSE) | [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) | [SECURITY.md](../SECURITY.md) | [CONTRIBUTING.md](../CONTRIBUTING.md) | [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md)

安全与社区文件已在源码仓库提供；解压包不包含全部社区文件，也不代表已完成全部许可审查。 Security and community files are available in the source repository; the extracted package does not contain every community file, and this is not a completed all-license audit.
