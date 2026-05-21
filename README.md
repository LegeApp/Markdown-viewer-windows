# Markdown Viewer (WPF)

High-performance Markdown document viewer for Windows built entirely with WPF.

## Architecture

- **MarkdownViewer.Wpf** (`net9.0-windows` / `net10.0-windows`): WPF application using `FlowDocument` for rendering. Markdown is parsed by [Markdig](https://github.com/xoofx/markdig) (local source checkout) into an AST, then converted to WPF document objects with full styling support (headings, lists, tables, code blocks, blockquotes, images, links, inline formatting, and more).
- **markdig** (local source via `ProjectReference`): The underlying markdown parser.

No native C++ components. All rendering is pure WPF.

## Prerequisites (Windows)

1. Install **.NET 9 SDK** (or .NET 10 SDK).
2. Install **Visual Studio 2022 (17.0) or newer** with the `.NET desktop development` workload.

## Build & Run

Open `MarkdownViewer.sln` in Visual Studio, or use the CLI:

```powershell
dotnet run --project MarkdownViewer.Wpf\MarkdownViewer.Wpf.csproj -c Release
```

## Publish

```powershell
dotnet publish .\MarkdownViewer.Wpf\MarkdownViewer.Wpf.csproj -c Release -o .\publish
```

A clean publish folder contains a single file: `publish\MarkdownViewer.exe`

## Opening Files From Explorer

`MarkdownViewer.exe` accepts file paths directly from the shell, so double-clicking an `.md` or `.markdown` file opens it immediately in the viewer. Drag and drop onto the window is also supported.

## File Association Setup

The viewer can register itself as a per-user Markdown handler:

```powershell
.\MarkdownViewer.exe --register-associations
```

To remove that registration:

```powershell
.\MarkdownViewer.exe --unregister-associations
```

This registration adds the app to the Windows "Open with" / default-app flow for `.md` and `.markdown`.

Windows 10/11 may still require one manual confirmation in the UI because the OS protects default-app selection. After running the registration command, use either:

1. Right-click a Markdown file, choose `Open with`, browse to `MarkdownViewer.exe`, and check `Always use this app`.
2. Or go to `Settings > Apps > Default apps` and choose `MarkdownViewer` for `.md` / `.markdown`.

## Features

- Open `.md` / `.markdown` / `.txt` files
- Parse with local Markdig source reference
- Pure WPF `FlowDocument` rendering with full styling
- Scroll large documents
- Link hit-test and open in browser
- Search next/previous (paragraph-level navigation)
- Light/dark theme toggle
- Drag and drop file loading
- Command-line file opening
- File association registration
- Images rendered inline

## Known Limitations

- Search highlighting is not yet rendered visually in the document.
- No edit mode / split view.
