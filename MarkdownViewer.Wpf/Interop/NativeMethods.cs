using MarkdownViewer.Wpf.Models;
using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace MarkdownViewer.Wpf.Interop;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MdSpanInterop
    {
        public uint Start;
        public uint Length;
        public uint StyleFlags;
        public uint Rgba;
        public uint LinkId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MdParagraphInterop
    {
        public IntPtr Text;
        public uint TextLen;
        public BlockType BlockType;
        public IntPtr Spans;
        public uint SpanCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MdHitTestResult
    {
        public uint ParagraphIndex;
        public uint TextPos;
        public uint Trailing;
        public uint LinkId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MdThemeInterop
    {
        public uint Background;
        public uint Foreground;
        public uint Link;
        public uint CodeBackground;
        public uint QuoteBar;
        public uint Selection;
    }

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr MdCreateRenderer(IntPtr parent, int x, int y, int w, int h);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdDestroyRenderer(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr MdGetHwnd(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdResizeRenderer(IntPtr handle, int w, int h);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdInvalidate(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdSetScrollY(IntPtr handle, float scrollY);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern float MdGetScrollY(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern float MdGetContentHeight(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdSetDocument(IntPtr handle, IntPtr paragraphs, uint count);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int MdHitTest(IntPtr handle, float x, float y, out MdHitTestResult result);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint MdConsumeActivatedLink(IntPtr handle);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern float MdGetParagraphY(IntPtr handle, uint paragraphIndex);

    [DllImport("MarkdownViewer.Native.dll", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void MdSetTheme(IntPtr handle, MdThemeInterop theme);
}
