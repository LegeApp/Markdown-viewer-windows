using MarkdownViewer.Wpf.NativeHost;
using MarkdownViewer.Wpf.ViewModels;
using System.Windows.Controls;

namespace MarkdownViewer.Wpf.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is IMainViewModel vm)
        {
            vm.AttachRenderer(RendererHost);
        }
    }
}
