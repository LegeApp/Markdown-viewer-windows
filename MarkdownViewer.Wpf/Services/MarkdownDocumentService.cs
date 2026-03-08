using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownViewer.Wpf.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Wpf.Services;

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public Task<MarkdownRenderDocument> ParseAsync(string markdown, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Parse(markdown, cancellationToken), cancellationToken);
    }

    public async Task<MarkdownRenderDocument> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var markdown = await File.ReadAllTextAsync(filePath, cancellationToken);
        return await ParseAsync(markdown, cancellationToken);
    }

    private MarkdownRenderDocument Parse(string markdown, CancellationToken cancellationToken)
    {
        var doc = Markdig.Markdown.Parse(markdown, _pipeline);
        var paragraphs = new List<Paragraph>(Math.Max(16, doc.Count));
        var links = new Dictionary<uint, string>();
        uint nextLinkId = 1;

        foreach (var block in doc)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendBlock(block, paragraphs, links, ref nextLinkId, cancellationToken);
        }

        return new MarkdownRenderDocument(paragraphs.ToArray(), links);
    }

    private static void AppendBlock(
        Block block,
        List<Paragraph> paragraphs,
        Dictionary<uint, string> links,
        ref uint nextLinkId,
        CancellationToken cancellationToken)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var (text, spans) = CollectInlines(heading.Inline, links, ref nextLinkId);
                var type = heading.Level switch
                {
                    1 => BlockType.Heading1,
                    2 => BlockType.Heading2,
                    _ => BlockType.Heading3
                };
                paragraphs.Add(new Paragraph(text, type, spans.ToArray()));
                break;
            }
            case ParagraphBlock para:
            {
                var (text, spans) = CollectInlines(para.Inline, links, ref nextLinkId);
                paragraphs.Add(new Paragraph(text, BlockType.Paragraph, spans.ToArray()));
                break;
            }
            case QuoteBlock quote:
            {
                foreach (var child in quote)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var before = paragraphs.Count;
                    AppendBlock(child, paragraphs, links, ref nextLinkId, cancellationToken);
                    for (var i = before; i < paragraphs.Count; i++)
                    {
                        var p = paragraphs[i];
                        paragraphs[i] = p with { BlockType = BlockType.Quote };
                    }
                }
                break;
            }
            case ListBlock listBlock:
            {
                var index = 1;
                foreach (var item in listBlock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item is not ListItemBlock li)
                    {
                        continue;
                    }

                    var marker = listBlock.IsOrdered ? $"{index}. " : "- ";
                    index++;
                    foreach (var child in li)
                    {
                        var before = paragraphs.Count;
                        AppendBlock(child, paragraphs, links, ref nextLinkId, cancellationToken);
                        if (paragraphs.Count <= before)
                        {
                            continue;
                        }

                        var p = paragraphs[before];
                        var shiftedSpans = new List<Span>(p.Spans.Length);
                        foreach (var span in p.Spans)
                        {
                            shiftedSpans.Add(span with { Start = span.Start + (uint)marker.Length });
                        }

                        paragraphs[before] = new Paragraph(marker + p.Text, BlockType.ListItem, shiftedSpans.ToArray());
                    }
                }
                break;
            }
            case FencedCodeBlock fenced:
            {
                paragraphs.Add(new Paragraph(GetLeafBlockText(fenced), BlockType.CodeBlock, Array.Empty<Span>()));
                break;
            }
            case CodeBlock codeBlock:
            {
                paragraphs.Add(new Paragraph(GetLeafBlockText(codeBlock), BlockType.CodeBlock, Array.Empty<Span>()));
                break;
            }
            case Table table:
            {
                foreach (var rowObj in table)
                {
                    if (rowObj is not TableRow row)
                    {
                        continue;
                    }

                    var rowSb = new StringBuilder();
                    var first = true;
                    foreach (var cellObj in row)
                    {
                        if (cellObj is not TableCell cell)
                        {
                            continue;
                        }

                        if (!first)
                        {
                            rowSb.Append(" | ");
                        }
                        first = false;

                        foreach (var cellBlock in cell)
                        {
                            if (cellBlock is ParagraphBlock cellParagraph)
                            {
                                var (text, _) = CollectInlines(cellParagraph.Inline, links, ref nextLinkId);
                                rowSb.Append(text);
                            }
                        }
                    }

                    paragraphs.Add(new Paragraph(rowSb.ToString(), BlockType.TableRow, Array.Empty<Span>()));
                }
                break;
            }
        }
    }

    private static string GetLeafBlockText(LeafBlock block)
    {
        var sb = new StringBuilder();
        foreach (var line in block.Lines.Lines)
        {
            var lineText = line.Slice.Text;
            if (lineText is null)
            {
                continue;
            }

            sb.Append(line.Slice.ToString());
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static (string Text, IReadOnlyList<Span> Spans) CollectInlines(ContainerInline? container, Dictionary<uint, string> links, ref uint nextLinkId)
    {
        if (container is null)
        {
            return (string.Empty, Array.Empty<Span>());
        }

        var sb = new StringBuilder();
        var spans = new List<Span>();
        AppendInlineChildren(container, sb, spans, StyleFlags.None, links, ref nextLinkId);
        return (sb.ToString(), spans);
    }

    private static void AppendInlineChildren(
        ContainerInline parent,
        StringBuilder sb,
        List<Span> spans,
        StyleFlags inheritedFlags,
        Dictionary<uint, string> links,
        ref uint nextLinkId)
    {
        for (var inline = parent.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                {
                    var start = sb.Length;
                    var text = literal.Content.ToString();
                    sb.Append(text);
                    AddSpan(spans, start, text.Length, inheritedFlags, 0, 0xFFFFFFFF);
                    break;
                }
                case LineBreakInline:
                    sb.Append('\n');
                    break;
                case CodeInline code:
                {
                    var start = sb.Length;
                    var text = code.Content;
                    sb.Append(text);
                    AddSpan(spans, start, text.Length, inheritedFlags | StyleFlags.Code, 0, 0xFF2D2D2D);
                    break;
                }
                case EmphasisInline emphasis:
                {
                    var flags = inheritedFlags;
                    if (emphasis.DelimiterChar == '~')
                    {
                        flags |= StyleFlags.Strikethrough;
                    }
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        flags |= StyleFlags.Bold;
                    }
                    else
                    {
                        flags |= StyleFlags.Italic;
                    }

                    AppendInlineChildren(emphasis, sb, spans, flags, links, ref nextLinkId);
                    break;
                }
                case LinkInline linkInline:
                {
                    uint linkId = 0;
                    if (!string.IsNullOrWhiteSpace(linkInline.Url))
                    {
                        linkId = nextLinkId++;
                        links[linkId] = linkInline.Url;
                    }

                    var flags = inheritedFlags | StyleFlags.Link;
                    var before = sb.Length;
                    AppendInlineChildren(linkInline, sb, spans, flags, links, ref nextLinkId);
                    var len = sb.Length - before;
                    AddSpan(spans, before, len, flags, linkId, 0xFF0A66CC);
                    break;
                }
                case HtmlInline html:
                {
                    var start = sb.Length;
                    var text = html.Tag;
                    sb.Append(text);
                    AddSpan(spans, start, text.Length, inheritedFlags, 0, 0xFF444444);
                    break;
                }
                case ContainerInline nested:
                    AppendInlineChildren(nested, sb, spans, inheritedFlags, links, ref nextLinkId);
                    break;
            }
        }
    }

    private static void AddSpan(List<Span> spans, int start, int length, StyleFlags flags, uint linkId, uint rgba)
    {
        if (length <= 0 || flags == StyleFlags.None)
        {
            return;
        }

        spans.Add(new Span((uint)start, (uint)length, flags, rgba, linkId));
    }
}
