using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;
using MdHeadingBlock = Markdig.Syntax.HeadingBlock;
using MdParagraphBlock = Markdig.Syntax.ParagraphBlock;
using MdQuoteBlock = Markdig.Syntax.QuoteBlock;
using MdListBlock = Markdig.Syntax.ListBlock;
using MdListItemBlock = Markdig.Syntax.ListItemBlock;
using MdCodeBlock = Markdig.Syntax.CodeBlock;
using MdFencedCodeBlock = Markdig.Syntax.FencedCodeBlock;
using MdThematicBreakBlock = Markdig.Syntax.ThematicBreakBlock;
using MdHtmlBlock = Markdig.Syntax.HtmlBlock;
using MdLeafBlock = Markdig.Syntax.LeafBlock;

namespace MarkdownViewer.Wpf.Services;

public sealed class MarkdownDocumentService : IMarkdownDocumentService
{
    private static readonly FileStreamOptions FileStreamOptions = new()
    {
        Access = FileAccess.Read,
        Mode = FileMode.Open,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        Share = FileShare.ReadWrite | FileShare.Delete,
        BufferSize = 64 * 1024
    };

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public async Task<MarkdownDocument> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileStreamOptions);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var markdown = await reader.ReadToEndAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => Markdig.Markdown.Parse(markdown, _pipeline), cancellationToken);
    }

    public FlowDocument BuildFlowDocument(MarkdownDocument ast, bool isDarkTheme)
    {
        var theme = isDarkTheme ? Theme.Dark : Theme.Light;
        var baseUri = ast.GetData("BaseUri") as Uri;

        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Foreground = theme.Foreground,
            Background = theme.Background,
            PagePadding = new Thickness(28, 18, 28, 18),
            TextAlignment = TextAlignment.Left,
            IsHyphenationEnabled = false,
        };

        var ctx = new BuildContext(theme, baseUri);
        AppendBlocks(flow.Blocks, ast, ctx);
        return flow;
    }

    private static void AppendBlocks(BlockCollection target, ContainerBlock source, BuildContext ctx)
    {
        foreach (var block in source)
        {
            var converted = ConvertBlock(block, ctx);
            if (converted is not null)
            {
                target.Add(converted);
            }
        }
    }

    private static WpfBlock? ConvertBlock(Markdig.Syntax.Block block, BuildContext ctx)
    {
        switch (block)
        {
            case MdHeadingBlock h:
                return BuildHeading(h, ctx);

            case MdParagraphBlock p:
                return BuildParagraph(p, ctx);

            case MdQuoteBlock q:
                return BuildQuote(q, ctx);

            case MdListBlock list:
                return BuildList(list, ctx);

            case MdFencedCodeBlock fenced:
                return BuildCodeBlock(fenced, fenced.Info, ctx);

            case MdCodeBlock code:
                return BuildCodeBlock(code, null, ctx);

            case Markdig.Extensions.Tables.Table table:
                return BuildTable(table, ctx);

            case MdThematicBreakBlock:
                return BuildHorizontalRule(ctx);

            case MdHtmlBlock html:
                return BuildHtmlBlock(html, ctx);

            default:
                return null;
        }
    }

    private static WpfParagraph BuildHeading(MdHeadingBlock heading, BuildContext ctx)
    {
        var para = new WpfParagraph
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = heading.Level switch
            {
                1 => 30,
                2 => 24,
                3 => 20,
                4 => 17,
                5 => 15,
                _ => 14,
            },
            Margin = new Thickness(0, heading.Level == 1 ? 4 : 18, 0, 6),
        };

        if (heading.Level <= 2)
        {
            para.BorderBrush = ctx.Theme.RuleBrush;
            para.BorderThickness = new Thickness(0, 0, 0, 1);
            para.Padding = new Thickness(0, 0, 0, 4);
        }

        AppendInlines(para.Inlines, heading.Inline, ctx);
        return para;
    }

    private static WpfParagraph BuildParagraph(MdParagraphBlock paragraph, BuildContext ctx)
    {
        var para = new WpfParagraph
        {
            Margin = new Thickness(0, 0, 0, 10),
        };
        AppendInlines(para.Inlines, paragraph.Inline, ctx);
        return para;
    }

    private static WpfBlock BuildQuote(MdQuoteBlock quote, BuildContext ctx)
    {
        var section = new Section
        {
            BorderBrush = ctx.Theme.QuoteBar,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 4, 0, 4),
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = ctx.Theme.Muted,
        };
        AppendBlocks(section.Blocks, quote, ctx);
        return section;
    }

    private static WpfBlock BuildList(MdListBlock list, BuildContext ctx)
    {
        var wpfList = new WpfList
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(28, 0, 0, 0),
        };

        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start >= 1)
        {
            wpfList.StartIndex = start;
        }

        foreach (var child in list)
        {
            if (child is not MdListItemBlock itemBlock)
            {
                continue;
            }

            var item = new ListItem();
            AppendBlocks(item.Blocks, itemBlock, ctx);

            if (item.Blocks.Count == 0)
            {
                item.Blocks.Add(new WpfParagraph());
            }

            foreach (var b in item.Blocks)
            {
                if (b is WpfParagraph p)
                {
                    p.Margin = new Thickness(0, 0, 0, 2);
                }
            }

            wpfList.ListItems.Add(item);
        }

        return wpfList;
    }

    private static WpfParagraph BuildCodeBlock(MdLeafBlock block, string? language, BuildContext ctx)
    {
        var text = GetLeafBlockText(block);
        var para = new WpfParagraph
        {
            FontFamily = ctx.Theme.Monospace,
            FontSize = 13,
            Background = ctx.Theme.CodeBackground,
            Foreground = ctx.Theme.Foreground,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 4, 0, 12),
            TextAlignment = TextAlignment.Left,
        };
        para.Inlines.Add(new Run(text));
        if (!string.IsNullOrWhiteSpace(language))
        {
            para.Tag = language;
        }
        return para;
    }

    private static WpfBlock BuildTable(Markdig.Extensions.Tables.Table table, BuildContext ctx)
    {
        var wpfTable = new WpfTable
        {
            CellSpacing = 0,
            BorderBrush = ctx.Theme.RuleBrush,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 14),
        };

        var columnCount = 0;
        foreach (var rowObj in table)
        {
            if (rowObj is Markdig.Extensions.Tables.TableRow row && row.Count > columnCount)
            {
                columnCount = row.Count;
            }
        }

        for (var i = 0; i < columnCount; i++)
        {
            wpfTable.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        wpfTable.RowGroups.Add(rowGroup);

        foreach (var rowObj in table)
        {
            if (rowObj is not Markdig.Extensions.Tables.TableRow row)
            {
                continue;
            }

            var wpfRow = new WpfTableRow();
            if (row.IsHeader)
            {
                wpfRow.Background = ctx.Theme.TableHeaderBackground;
                wpfRow.FontWeight = FontWeights.SemiBold;
            }

            var columnIndex = 0;
            foreach (var cellObj in row)
            {
                if (cellObj is not Markdig.Extensions.Tables.TableCell cell)
                {
                    continue;
                }

                var alignment = ResolveCellAlignment(table, columnIndex);
                var wpfCell = new WpfTableCell
                {
                    BorderBrush = ctx.Theme.RuleBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 4, 8, 4),
                    TextAlignment = alignment,
                };

                AppendBlocks(wpfCell.Blocks, cell, ctx);
                foreach (var b in wpfCell.Blocks)
                {
                    if (b is WpfParagraph p)
                    {
                        p.Margin = new Thickness(0);
                        p.TextAlignment = alignment;
                    }
                }

                wpfRow.Cells.Add(wpfCell);
                columnIndex++;
            }

            rowGroup.Rows.Add(wpfRow);
        }

        return wpfTable;
    }

    private static TextAlignment ResolveCellAlignment(Markdig.Extensions.Tables.Table table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[columnIndex].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    private static WpfBlock BuildHorizontalRule(BuildContext ctx)
    {
        var rule = new BlockUIContainer(new Border
        {
            BorderBrush = ctx.Theme.RuleBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 6, 0, 10),
            Height = 1,
        });
        return rule;
    }

    private static WpfBlock BuildHtmlBlock(MdHtmlBlock html, BuildContext ctx)
    {
        var text = GetLeafBlockText(html);
        var para = new WpfParagraph
        {
            FontFamily = ctx.Theme.Monospace,
            FontSize = 12,
            Foreground = ctx.Theme.Muted,
            Margin = new Thickness(0, 0, 0, 10),
        };
        para.Inlines.Add(new Run(text));
        return para;
    }

    private static void AppendInlines(InlineCollection target, ContainerInline? container, BuildContext ctx)
    {
        if (container is null)
        {
            return;
        }

        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            AppendInline(target, inline, ctx);
        }
    }

    private static void AppendInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline, BuildContext ctx)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run(literal.Content.ToString()));
                break;

            case LineBreakInline lineBreak:
                target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                break;

            case CodeInline code:
            {
                var run = new Run(code.Content)
                {
                    FontFamily = ctx.Theme.Monospace,
                    Background = ctx.Theme.InlineCodeBackground,
                };
                target.Add(run);
                break;
            }

            case EmphasisInline emphasis:
            {
                var span = new Span();
                if (emphasis.DelimiterChar == '~')
                {
                    span.TextDecorations = TextDecorations.Strikethrough;
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.Bold;
                }
                else
                {
                    span.FontStyle = FontStyles.Italic;
                }

                foreach (var child in emphasis)
                {
                    AppendInline(span.Inlines, child, ctx);
                }
                target.Add(span);
                break;
            }

            case LinkInline link when link.IsImage:
            {
                var image = TryCreateImage(link.Url, ctx);
                if (image is not null)
                {
                    target.Add(new InlineUIContainer(image));
                }
                else
                {
                    var span = new Span { FontStyle = FontStyles.Italic, Foreground = ctx.Theme.Muted };
                    foreach (var child in link)
                    {
                        AppendInline(span.Inlines, child, ctx);
                    }
                    target.Add(span);
                }
                break;
            }

            case LinkInline link:
            {
                var hyperlink = new Hyperlink
                {
                    Foreground = ctx.Theme.LinkBrush,
                    ToolTip = link.Url,
                };

                if (Uri.TryCreate(link.Url, UriKind.RelativeOrAbsolute, out var uri))
                {
                    hyperlink.NavigateUri = uri;
                }

                hyperlink.RequestNavigate += OnHyperlinkRequestNavigate;

                foreach (var child in link)
                {
                    AppendInline(hyperlink.Inlines, child, ctx);
                }
                target.Add(hyperlink);
                break;
            }

            case TaskList task:
                target.Add(new Run(task.Checked ? "☑ " : "☐ ") { FontFamily = ctx.Theme.Monospace });
                break;

            case HtmlInline html:
                target.Add(new Run(html.Tag) { Foreground = ctx.Theme.Muted });
                break;

            case AutolinkInline auto:
            {
                var hyperlink = new Hyperlink(new Run(auto.Url))
                {
                    Foreground = ctx.Theme.LinkBrush,
                    ToolTip = auto.Url,
                };
                if (Uri.TryCreate(auto.Url, UriKind.RelativeOrAbsolute, out var uri))
                {
                    hyperlink.NavigateUri = uri;
                }
                hyperlink.RequestNavigate += OnHyperlinkRequestNavigate;
                target.Add(hyperlink);
                break;
            }

            case ContainerInline nested:
            {
                foreach (var child in nested)
                {
                    AppendInline(target, child, ctx);
                }
                break;
            }
        }
    }

    private static Image? TryCreateImage(string? url, BuildContext ctx)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
            }
            else if (ctx.BaseUri is not null && Uri.TryCreate(ctx.BaseUri, url, out var relative))
            {
                uri = relative;
            }
            else
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();

            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 800,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        var url = e.Uri?.ToString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort: launching is the user's intent; failures (no browser, unsupported scheme) just no-op.
        }
        e.Handled = true;
    }

    private static string GetLeafBlockText(MdLeafBlock block)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var line in block.Lines.Lines)
        {
            if (line.Slice.Text is null)
            {
                continue;
            }

            if (!first)
            {
                sb.Append('\n');
            }
            first = false;
            sb.Append(line.Slice.ToString());
        }
        return sb.ToString();
    }

    private sealed record BuildContext(Theme Theme, Uri? BaseUri);

    private sealed class Theme
    {
        public required Brush Background { get; init; }
        public required Brush Foreground { get; init; }
        public required Brush Muted { get; init; }
        public required Brush LinkBrush { get; init; }
        public required Brush CodeBackground { get; init; }
        public required Brush InlineCodeBackground { get; init; }
        public required Brush QuoteBar { get; init; }
        public required Brush RuleBrush { get; init; }
        public required Brush TableHeaderBackground { get; init; }
        public required FontFamily Monospace { get; init; }

        private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

        public static Theme Light { get; } = new()
        {
            Background = Frozen(Color.FromRgb(0xFD, 0xFD, 0xFD)),
            Foreground = Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Muted = Frozen(Color.FromRgb(0x60, 0x60, 0x60)),
            LinkBrush = Frozen(Color.FromRgb(0x0A, 0x66, 0xCC)),
            CodeBackground = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF4)),
            InlineCodeBackground = Frozen(Color.FromRgb(0xEC, 0xEC, 0xEC)),
            QuoteBar = Frozen(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            RuleBrush = Frozen(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            TableHeaderBackground = Frozen(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            Monospace = MonoFont,
        };

        public static Theme Dark { get; } = new()
        {
            Background = Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = Frozen(Color.FromRgb(0xF1, 0xF1, 0xF1)),
            Muted = Frozen(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            LinkBrush = Frozen(Color.FromRgb(0x66, 0xAF, 0xFF)),
            CodeBackground = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            InlineCodeBackground = Frozen(Color.FromRgb(0x33, 0x33, 0x33)),
            QuoteBar = Frozen(Color.FromRgb(0x55, 0x55, 0x55)),
            RuleBrush = Frozen(Color.FromRgb(0x40, 0x40, 0x40)),
            TableHeaderBackground = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            Monospace = MonoFont,
        };

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
