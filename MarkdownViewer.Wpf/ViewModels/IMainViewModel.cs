using System.ComponentModel;
using System.Windows.Documents;

namespace MarkdownViewer.Wpf.ViewModels;

public interface IMainViewModel : INotifyPropertyChanged
{
    string CurrentFilePath { get; }
    bool HasDocument { get; }
    bool IsLoading { get; }
    string CurrentFileName { get; }
    string WindowTitle { get; }
    FlowDocument? Document { get; }

    void OpenFile(string filePath);
    void ToggleTheme();
    void SearchNext(string query);
    void SearchPrevious(string query);
}
