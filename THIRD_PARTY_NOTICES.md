# Third-party notices

AGEX release packages are self-contained and include the following third-party components. Each remains under its own licence; the full licence texts are available at the linked projects.

| Component | Version | Licence | Source |
| --- | --- | --- | --- |
| .NET runtime and base class libraries | 10.0 | MIT | https://github.com/dotnet/runtime |
| Avalonia (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Win32, Avalonia.Native, Avalonia.X11, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.Remote.Protocol) | 12.1.3 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Angle.Windows.Natives (ANGLE) | as referenced by Avalonia 12.1.3 | BSD-3-Clause | https://github.com/AvaloniaUI/angle, https://chromium.googlesource.com/angle/angle |
| SkiaSharp and native assets (Skia) | as referenced by Avalonia 12.1.3 | MIT (SkiaSharp), BSD-3-Clause (Skia) | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp and native assets (HarfBuzz) | as referenced by Avalonia 12.1.3 | MIT (HarfBuzzSharp), Old MIT (HarfBuzz) | https://github.com/mono/SkiaSharp, https://github.com/harfbuzz/harfbuzz |
| MicroCom.Runtime | as referenced by Avalonia 12.1.3 | MIT | https://github.com/kekekeks/MicroCom |
| Tmds.DBus.Protocol | as referenced by Avalonia 12.1.3 | MIT | https://github.com/tmds/Tmds.DBus |
| Inter font (Avalonia.Fonts.Inter) | 12.1.3 | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| Avalonia.Controls.WebView (uses the system web engine: WebView2, WKWebView or WebKitGTK; none is shipped with AGEX) | 12.1.0 | MIT | https://avaloniaui.net/ |

Used only to build and test AGEX, not shipped: xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Avalonia.BuildServices (build-time only; AGEX's scripts opt out of its telemetry).

## Skills

AGEX ships a catalog (`src/Agex.Core/Skills/skills-catalog.json`) that describes third-party skills; it does not contain their files. Skills are downloaded from their original repositories only when the user installs them, and remain under their own licences, shown on each skill card: obra/superpowers (MIT), openai/skills (Apache-2.0), anthropics/skills (Apache-2.0 for the skills listed), Microsoft Playwright MCP (Apache-2.0), Chrome DevTools MCP (Apache-2.0), Upstash Context7 MCP (MIT), Model Context Protocol reference servers (MIT), GitHub MCP Server (MIT).

## External programs

AGEX starts, but does not include, programs the user installed: the agent CLIs (Codex, Antigravity, Claude Code, Gemini CLI), Ollama, Git, and on macOS/Linux the system tools `security`, `osascript`, `open`, `secret-tool`, `notify-send` and `xdg-open`.

## Code from other projects

No source code was copied from other projects. Competitor projects reviewed for [reports/competitive-analysis.md](reports/competitive-analysis.md) were studied for features only.
