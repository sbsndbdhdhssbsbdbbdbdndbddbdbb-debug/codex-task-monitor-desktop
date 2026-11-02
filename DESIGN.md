# Desktop UI

Operate mode. White surfaces, neutral gray canvas, near-black primary actions, thin neutral separators. Green, amber and red communicate task state. Pet sprites retain their color. Microsoft YaHei UI / Segoe UI; titles 24px, sections 16px, body 13px, metadata 12px. Icons use the existing Windows MDL2 font.

Reference: Hchen1218/heytea-style, installed as heytea-doodle-poster. Translate its white field, strong black marks and single character anchor into a usable desktop interface. The current pet is shown large, above a slightly irregular baseline. Poster-specific distorted lettering and private study assets are not used as interface controls.

24px outer spacing, 8px gaps, 36-40px controls, 6-8px radii. No oversized heroes or floating section cards. Separate installed and discoverable pets with tabs, a scrollable collection and anchored selection preview. Download applies immediately and keeps the library open. Show success, failure and cancellation clearly.

Preserve foreground contrast on hover, especially primary buttons. Use a short content entrance and hover-only pet animation; honor Windows reduced animation. Process previews off the dispatcher, with bounded window-lifetime memory caching and no disk previews.

The floating task tray uses the same white/ink palette. Neutral white pet discs and an ink-on-white compact orb replace skin-colored chrome. Status is conveyed by a glyph and text, with restrained amber/red only for attention/failure. The original generated TaskPaperWorker PNG adds a small marker-drawn paper carrier beside the tray heading and in empty states. Keep the task rows clear of decorative art; long titles may wrap to two lines. Opening has one 150ms entrance, disabled with system/user animation preferences.

The main window is a master/detail workspace. A factual status strip precedes three category tabs, search, project filter and sorting. Task rows expose activity, elapsed time, relative activity time and child-task count. The right band shows the actual selected companion and recent completions by default; selecting a task replaces it with a compact companion header and scrollable full details. Selection and reading position survive unchanged-task refreshes. Empty searches have a clear-filter action. Paused and do-not-disturb states must be explicit and stay synchronized with the floating tray. Do not add percentages without a real progress source.
