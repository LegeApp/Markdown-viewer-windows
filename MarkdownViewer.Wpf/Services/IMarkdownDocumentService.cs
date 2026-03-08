using System.Threading;
using System.Threading.Tasks;
using MarkdownViewer.Wpf.Models;

namespace MarkdownViewer.Wpf.Services;

public interface IMarkdownDocumentService
{
    Task<MarkdownRenderDocument> ParseAsync(string markdown, CancellationToken cancellationToken = default);
    Task<MarkdownRenderDocument> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);
}
