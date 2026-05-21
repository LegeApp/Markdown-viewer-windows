using MarkdownViewer.Wpf.ViewModels;
using System.Linq;
using System.Windows;

namespace MarkdownViewer.Wpf;

public partial class MainWindow : Window
{
    private readonly IMainViewModel _viewModel;

    public MainWindow(IMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public void OpenFile(string filePath)
    {
        _viewModel.OpenFile(filePath);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedFiles || droppedFiles.Length == 0)
        {
            return;
        }

        var firstFile = droppedFiles.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstFile))
        {
            _viewModel.OpenFile(firstFile);
        }
    }
}
