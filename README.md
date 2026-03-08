# Markdown Viewer (WPF + Native DirectWrite)

First draft implementation of a high-performance Markdown reader for Windows.

## Projects

- `MarkdownViewer.Wpf` (`net10.0-windows`): WPF shell, Markdig parsing, search/theme/file-open UI, native host bridge.
- `MarkdownViewer.Native` (C++ DLL): DirectWrite/Direct2D markdown renderer hosted via `HwndHost`.
- `markdig` (local source checkout): referenced by `ProjectReference` from the WPF app.

## Prerequisites (Windows)

1. Install **.NET 10 SDK**.
2. Install **Visual Studio 2026 (18.0) or newer** with:
   - `.NET desktop development` workload (WPF)
   - `Desktop development with C++` workload (native renderer)
3. Ensure Windows SDK is installed (via C++ workload).

Official references:
- https://learn.microsoft.com/en-us/dotnet/core/install/windows
- https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs

## Build

1. Open `MarkdownViewer.sln` in Visual Studio.
2. Select `x64` platform.
3. Build `MarkdownViewer.Native` first.
4. Build and run `MarkdownViewer.Wpf`.

The WPF project copies `MarkdownViewer.Native.dll` from:

`MarkdownViewer.Native\\x64\\<Configuration>\\MarkdownViewer.Native.dll`

into app output automatically if present.

## MVP features implemented

- Open `.md` / `.markdown` files
- Parse with local Markdig source reference
- Native DirectWrite rendering via `HwndHost`
- Scroll large documents with paragraph layout caching
- Link hit-test and open in browser
- Search next/previous (paragraph-level navigation)
- Light/dark theme toggle

## Known limitations in first draft

- Search highlighting is not yet rendered natively.
- Table rendering is line-based text rows (readable, not GitHub pixel-match).
- No edit mode/split view.
