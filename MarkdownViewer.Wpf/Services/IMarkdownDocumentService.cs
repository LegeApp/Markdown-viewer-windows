using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using Markdig.Syntax;

namespace MarkdownViewer.Wpf.Services;

public interface IMarkdownDocumentService
{
    Task<MarkdownDocument> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);

    FlowDocument BuildFlowDocument(MarkdownDocument ast, bool isDarkTheme);
}
