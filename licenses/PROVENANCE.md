# Notice Sources / 声明来源

- `H.NotifyIcon-2.3.0.txt`: [upstream LICENSE.md](https://github.com/HavenDV/H.NotifyIcon/blob/9a5e0abd06fa5fd9e29541f002aaa1ec0bd8ecaa/LICENSE.md), the repository commit declared in the 2.3.0 NuGet packages. Also covers the H.NotifyIcon wrapper/generated-icon packages from that source.
- `Microsoft.Data.Sqlite.txt`: [upstream license](https://github.com/dotnet/efcore/blob/cdb9db79320fd091247c1c9548e6377f98d3ec5c/LICENSE.txt), the source commit declared by Microsoft.Data.Sqlite 8.0.33. This repository-wide MIT license retains the upstream Microsoft copyright notice.
- `SQLitePCLRaw-Apache-2.0.txt` and `SQLitePCLRaw-NOTICE.txt`: [upstream 2.x license and notice](https://github.com/ericsink/SQLitePCL.raw/tree/v2.1.12). The resolved 2.1.14 NuGet packages declare Apache-2.0; no matching 2.1.14 upstream Git tag was available during preparation. These are retained 2.x upstream license/notice texts, not a claim that every optional provider mentioned in that notice is bundled.
- Other dependency and runtime notices are copied from the restored official NuGet/runtime packages during publishing. The generated `licenses/dependencies.json` records exact resolved versions; it is an inventory, not a complete legal opinion or a list of every native component's individual license.

静态声明保留上游原始文本；分发版本及依赖以发布包清单为准。SQLitePCLRaw 的 2.1.14 包声明 Apache-2.0，但准备时未找到对应 Git 标签，故保留可核实的上游 2.x 许可和 NOTICE，并明确来源边界。上游通用 NOTICE 提到的全部可选组件不一定都在本程序中。它们不适用本项目自有代码的 MIT 授权替代。
