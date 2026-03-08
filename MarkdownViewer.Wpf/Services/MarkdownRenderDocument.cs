using System.Collections.Generic;
using MarkdownViewer.Wpf.Models;

namespace MarkdownViewer.Wpf.Services;

public record MarkdownRenderDocument(
    Paragraph[] Paragraphs,
    Dictionary<uint, string> LinkTargets
);
