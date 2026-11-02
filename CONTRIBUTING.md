# Contributing / 参与贡献

Chinese and English issues and pull requests are welcome. Please follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). This project is maintained on a best-effort basis; response times and new features are not guaranteed.

欢迎中英文 Issue 与 Pull Request，请遵守社区行为准则。项目为业余维护，不承诺固定响应时间或功能排期。

## Before reporting / 提交前

1. Search existing issues and use the current release where practical.
2. Reproduce with synthetic task names and non-private sample paths.
3. Include the version, Windows/display configuration, steps, expected behavior and actual behavior.
4. Remove usernames, email addresses, tokens, prompts, screenshots of real chats and private file paths.

先查已有问题，尽量用当前版本与虚构任务复现。说明版本、Windows/显示器配置、操作步骤、预期和实际结果。发布前遮蔽用户名、邮箱、令牌、提示词、真实对话截图与私人路径。不要直接上传原始诊断包。

For vulnerabilities, follow [SECURITY.md](SECURITY.md), not a public bug report.

## Development / 开发

```powershell
dotnet restore CodexTaskMonitor.sln
dotnet test CodexTaskMonitor.sln --configuration Release
dotnet run --project tests/CodexTaskMonitor.UiChecks -- "$PWD"
dotnet run --project tests/CodexTaskMonitor.UiChecks -- "$PWD" --floating
```

- Keep the user-facing model to three categories: In progress, Needs attention and Recently completed.
- Keep changes focused and add regression tests when task parsing, state transitions, image handling or storage changes.
- Do not execute scripts/commands embedded in pet packages. Preserve input limits and path checks.
- Do not commit personal pets, Codex data, monitor databases, tokens, settings, logs, local tool caches, private backups or generated binaries.
- Do not attach third-party artwork without permission covering the proposed use. Keep code and artwork licensing separate.
- Do not add personal paths or author email addresses to examples. Use environment variables and reserved example domains.

保留“进行中 / 需处理 / 最近完成”三个对外分类。修改解析、状态、图像或存储逻辑时补回归测试；不执行素材包中的脚本，不移除输入限制；不提交个人素材、Codex 数据、数据库、令牌、日志、工具缓存、私人备份和二进制产物。示例使用环境变量和保留示例域名，不写真实路径或个人邮箱。

Run the source/release checks before publishing. The release ZIP should contain only verified application files, documentation and third-party notices; never package the entire working folder.

发布前运行源码与发布包检查。ZIP 仅包含已核验程序、文档和第三方声明，不要把整个工作文件夹压缩发布。
