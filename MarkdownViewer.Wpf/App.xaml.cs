using MarkdownViewer.Wpf.Interop;
using MarkdownViewer.Wpf.Services;
using MarkdownViewer.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Windows;

namespace MarkdownViewer.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unexpected error: {args.Exception.Message}", "Markdown Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        try
        {
            var startupOptions = StartupOptions.Parse(e.Args);
            switch (startupOptions.Mode)
            {
                case StartupMode.RegisterAssociations:
                    FileAssociationRegistrar.RegisterCurrentUser();
                    MessageBox.Show(
                        "Markdown file associations were registered for this user.\n\nIf Windows does not switch the default app automatically, use Open with or Default apps to choose Markdown Viewer once.",
                        "Markdown Viewer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Shutdown();
                    return;
                case StartupMode.UnregisterAssociations:
                    FileAssociationRegistrar.UnregisterCurrentUser();
                    MessageBox.Show(
                        "Markdown Viewer file associations were removed for this user.",
                        "Markdown Viewer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Shutdown();
                    return;
            }

            _services = ConfigureServices();

            if (startupOptions.FilePaths.Count == 0)
            {
                ShowMainWindow(null);
                return;
            }

            foreach (var filePath in startupOptions.FilePaths)
            {
                ShowMainWindow(filePath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application startup failed: {ex.Message}", "Markdown Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMarkdownDocumentService, MarkdownDocumentService>();
        services.AddTransient<IMainViewModel, MainViewModel>();
        services.AddTransient<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void ShowMainWindow(string? initialFilePath)
    {
        var window = _services!.GetRequiredService<MainWindow>();
        if (MainWindow is null)
        {
            MainWindow = window;
        }

        window.Show();

        if (!string.IsNullOrWhiteSpace(initialFilePath))
        {
            window.OpenFile(initialFilePath);
        }
    }

    private sealed record StartupOptions(StartupMode Mode, IReadOnlyList<string> FilePaths)
    {
        public static StartupOptions Parse(IReadOnlyList<string> args)
        {
            var mode = StartupMode.OpenFiles;
            var filePaths = new List<string>();

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                switch (arg.Trim())
                {
                    case "--register":
                    case "/register":
                    case "--register-associations":
                        mode = StartupMode.RegisterAssociations;
                        break;
                    case "--unregister":
                    case "/unregister":
                    case "--unregister-associations":
                        mode = StartupMode.UnregisterAssociations;
                        break;
                    default:
                        filePaths.Add(arg);
                        break;
                }
            }

            return new StartupOptions(mode, filePaths);
        }
    }

    private enum StartupMode
    {
        OpenFiles,
        RegisterAssociations,
        UnregisterAssociations
    }
}
