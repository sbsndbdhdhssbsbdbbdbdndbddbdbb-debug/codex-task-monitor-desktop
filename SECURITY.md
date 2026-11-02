# Security / 安全说明

## Report privately / 私下报告

Do not put credentials, chat content, account details, raw logs, private paths or working exploit payloads in a public issue. Use this repository's **Security > Report a vulnerability** channel when available. If that channel is unavailable, open an issue containing only a request for a private reporting channel, without sensitive details. There is no guaranteed response time or paid support service.

请勿在公开 Issue 中提交密钥、聊天内容、账户信息、原始日志、私人路径或可直接利用的攻击样本。优先使用仓库 **Security > Report a vulnerability** 私密报告入口；入口不可用时，只提交不含敏感信息的联系请求。维护者不承诺固定响应时间。

Include the affected version, a minimal reproduction using synthetic data, expected/actual behavior and the potential impact. Keep sensitive supporting material private.

请提供受影响版本、使用虚构数据的最小复现、预期与实际行为及可能影响。敏感附件请通过私密渠道提供。

## Boundaries / 安全边界

- The monitor reads Codex's local session data. Its own database may contain task titles, project names, paths and activity excerpts. Treat that database as private.
- The optional local API uses a per-launch bearer token and binds to loopback. It is not intended for LAN/Internet exposure. Other processes running as the same user may be able to read local files; loopback authentication is not a sandbox.
- Privacy mode redacts selected notification/API/state-file fields. It is not complete anonymization of the UI, database, screenshots or earlier files.
- Browsing/downloading Petdex content makes third-party network requests. The upstream service receives normal connection information such as the IP address and requested resource.
- External pet content is untrusted input. Retain file-size and decoded-image limits; do not add script execution to pet packages.
- Never attach `integration.json`, `state.json`, `monitor.db`, a Codex session folder or a full settings/log archive to a public report.

程序自己的数据库可能保存任务标题、项目名、路径与活动摘要，请视为私人数据。回环 API 不应暴露到局域网或互联网，也不能防御同一用户权限下的恶意程序。隐私模式只遮蔽指定字段，不会彻底清理数据库、截图或旧文件。Petdex 联网请求会向第三方提供正常的连接信息。外部桌宠素材应作为不可信输入处理。

## Maintenance / 维护范围

This is an independent community project, not an OpenAI or Petdex product. Security fixes target the latest source and explicitly published release. Older binaries are not promised ongoing support. Source availability does not guarantee that a binary is safe; verify its provenance and published checksums, and keep operating-system protections enabled.

This preview targets .NET 8. Microsoft lists its support end as 2026-11-10. A move to a supported framework is required for continued security maintenance after that date; bundling the runtime does not extend its support lifecycle. See the [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

这是独立社区项目，非 OpenAI 或 Petdex 官方产品。安全修复面向最新源码和明确发布的版本，不承诺持续维护旧版二进制文件。开源不等于二进制文件自动安全，请核对来源与校验和，并保留系统安全保护。

本预览版使用 .NET 8，官方支持截止日期为 2026-11-10。之后继续维护需要迁移到受支持的框架；自包含打包不延长运行时的支持周期。
