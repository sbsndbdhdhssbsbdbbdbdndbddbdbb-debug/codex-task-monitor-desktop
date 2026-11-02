# Third-Party Notices / 第三方声明

## Scope / 适用范围

The root MIT license applies to this project's own code and explicitly identified original assets, not to every dependency or externally downloaded pet. Dependency licenses and notices must be retained when distributing binaries. The release includes a dependency inventory and copied notices under `licenses/`.

根目录 MIT 许可适用于本项目自有代码及明确列出的原创资源，不会自动覆盖依赖库或外部下载的桌宠。分发二进制文件时需保留相应依赖许可；发布包的 `licenses/` 目录包含依赖清单与随包声明。

## Runtime and libraries / 运行时与依赖

- [.NET / WPF / ASP.NET Core](https://github.com/dotnet): see the runtime licenses and third-party notices included with the release.
- [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore): MIT; SQLite-related native and wrapper dependencies retain their own notices.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MIT.
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon): MIT.
- [SkiaSharp 4.154.0 and Windows native assets](https://github.com/mono/SkiaSharp/releases/tag/v4.154.0): MIT; the native package also includes separate upstream third-party notices, retained in the release.
- Exact resolved dependency versions are listed in the generated release inventory. Do not assume all transitive packages share the root license.

上述组件和它们的传递依赖各有许可，确切版本以发布包清单为准。开源源码与自包含二进制包的许可材料需分别检查。

## Original UI assets / 本项目资源

The three terminal-robot sprite sheets are produced by the source scripts in `scripts/`. `TaskPaperWorker.png` is original AI-generated UI artwork documented in `src/CodexTaskMonitor.App/Assets/README.md`. Neither the generated artwork nor the UI style references claim official HEYTEA, OpenAI or Petdex affiliation. Other bundled marks are the project's own application icon.

三个终端机器人动画表由仓库内脚本绘制；手绘风任务插图的生成来源见资源说明。视觉风格参考不表示获得任何品牌背书，也不表示与喜茶、OpenAI 或 Petdex 存在官方关联。

## Petdex and character artwork / Petdex 与角色素材

Petdex's platform code and the artwork in its gallery are distinct. Petdex documents a public catalog API for third-party clients, but individual pet assets follow their authors' declared licenses. Community review or free download is not proof of authorization from an underlying character-IP owner.

Petdex 平台代码与图库图片是不同内容。平台提供第三方客户端接入接口，但具体素材适用作者声明的许可；社区审核或免费可下载，不等于已经取得原角色权利人的授权。

- External pets are downloaded only on user action; transient previews also make requests to upstream resources.
- Personal downloaded pets are not included in this source repository or official release ZIP.
- Preserve attribution, source links and any permission/license files provided with a pet. Unknown licensing must not be represented as MIT, public domain or unrestricted commercial use.
- A user's ability to download a file does not transfer its copyright to the project or the user.
- Before redistributing artwork, publishing promotional screenshots featuring it, or building a curated collection, verify permission for that use and any underlying IP rights.
- Rights concerns may be raised with maintainers using the private channel described in `SECURITY.md`. Maintainers should review credible notices and stop any project-controlled distribution or links as appropriate. This procedure does not itself supply missing authorization.

外部素材及其角色权利不因下载而转让。本仓库与正式发布包不附送个人下载的素材；请保留作者、来源及原有许可。授权不明时不要写成“MIT / 公有领域 / 无限制商用”。再分发、宣传截图或整理合集前，应确认相应用途与原角色权利。投诉处理不是免责保证。

Sources: [Petdex integration](https://github.com/crafter-station/petdex#for-builders), [Petdex licensing and takedowns](https://github.com/crafter-station/petdex#pet-ip-and-takedowns).
