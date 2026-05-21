using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MarkdownViewer.Wpf.Interop;

internal static class FileAssociationRegistrar
{
    private const string ProgId = "MarkdownViewer.MarkdownFile";

    public static void RegisterCurrentUser()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current executable path.");
        var applicationId = Path.GetFileName(executablePath);
        var escapedCommand = $"\"{executablePath}\" \"%1\"";

        using (var applicationsKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{applicationId}"))
        {
            applicationsKey?.SetValue(string.Empty, "Markdown Viewer");

            using var commandKey = applicationsKey?.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(string.Empty, escapedCommand);

            using var supportedTypesKey = applicationsKey?.CreateSubKey("SupportedTypes");
            supportedTypesKey?.SetValue(".md", string.Empty);
            supportedTypesKey?.SetValue(".markdown", string.Empty);
        }

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey?.SetValue(string.Empty, "Markdown Document");

            using var iconKey = progIdKey?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue(string.Empty, $"\"{executablePath}\",0");

            using var commandKey = progIdKey?.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue(string.Empty, escapedCommand);
        }

        RegisterExtension(".md");
        RegisterExtension(".markdown");
        NotifyShellAssociationsChanged();
    }

    public static void UnregisterCurrentUser()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current executable path.");
        var applicationId = Path.GetFileName(executablePath);

        UnregisterExtension(".md");
        UnregisterExtension(".markdown");

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{applicationId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        NotifyShellAssociationsChanged();
    }

    private static void RegisterExtension(string extension)
    {
        using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
        if (extensionKey is null)
        {
            throw new IOException($"Unable to create the registry key for '{extension}'.");
        }

        extensionKey.SetValue(string.Empty, ProgId);

        using var openWithKey = extensionKey.CreateSubKey("OpenWithProgids");
        openWithKey?.SetValue(ProgId, string.Empty, RegistryValueKind.String);
    }

    private static void UnregisterExtension(string extension)
    {
        using var extensionKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}", writable: true);
        if (extensionKey is null)
        {
            return;
        }

        var currentDefault = extensionKey.GetValue(string.Empty) as string;
        if (string.Equals(currentDefault, ProgId, StringComparison.OrdinalIgnoreCase))
        {
            extensionKey.DeleteValue(string.Empty, throwOnMissingValue: false);
        }

        using var openWithKey = extensionKey.OpenSubKey("OpenWithProgids", writable: true);
        openWithKey?.DeleteValue(ProgId, throwOnMissingValue: false);
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
