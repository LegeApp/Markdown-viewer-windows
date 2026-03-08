using MarkdownViewer.Wpf.ViewModels;
using System.Windows;

namespace MarkdownViewer.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(IMainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
