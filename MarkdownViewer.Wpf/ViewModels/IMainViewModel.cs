using System.ComponentModel;
using MarkdownViewer.Wpf.Models;
using MarkdownViewer.Wpf.NativeHost;

namespace MarkdownViewer.Wpf.ViewModels;

public interface IMainViewModel : INotifyPropertyChanged
{
    string CurrentFilePath { get; }
    bool HasDocument { get; }
    bool IsLoading { get; }
    Theme CurrentTheme { get; }
    string CurrentFileName { get; }
    double ScrollPosition { get; set; }
    double ContentHeight { get; }

    void AttachRenderer(IMarkdownRendererBridge renderer);
    void OpenFile(string filePath);
    void ToggleTheme();
    void SearchNext(string query);
    void SearchPrevious(string query);
}
