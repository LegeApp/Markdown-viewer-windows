using System;

namespace MarkdownViewer.Wpf.Models;

public enum BlockType
{
    Paragraph = 0,
    Heading1 = 1,
    Heading2 = 2,
    Heading3 = 3,
    CodeBlock = 4,
    Quote = 5,
    ListItem = 6,
    TableRow = 7,
}

[Flags]
public enum StyleFlags
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Code = 1 << 2,
    Link = 1 << 3,
    Strikethrough = 1 << 4,
}

public record Span(
    uint Start,
    uint Length,
    StyleFlags StyleFlags,
    uint Rgba,
    uint LinkId
);

public record Paragraph(
    string Text,
    BlockType BlockType,
    Span[] Spans
);

public record Theme(
    uint Background,
    uint Foreground,
    uint Link,
    uint CodeBackground,
    uint QuoteBar,
    uint Selection
);

public record HitTestResult(
    uint ParagraphIndex,
    uint TextPos,
    uint Trailing,
    uint LinkId
);
