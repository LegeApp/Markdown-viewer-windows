using MarkdownViewer.Wpf.Interop;
using MarkdownViewer.Wpf.Models;
using MarkdownViewer.Wpf.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MarkdownViewer.Wpf.NativeHost;

public sealed class MarkdownRendererHost : HwndHost, IMarkdownRendererBridge
{
    private IntPtr _rendererHandle;
    private IntPtr _childHwnd;
    private readonly DispatcherTimer _linkTimer;
    private IReadOnlyDictionary<uint, string> _linkTargets = new Dictionary<uint, string>();

    public event Action<uint>? LinkActivated;

    public new IntPtr Handle => _childHwnd;

    public MarkdownRendererHost()
    {
        _linkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _linkTimer.Tick += (_, _) => PollActivatedLink();
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var width = Math.Max((int)ActualWidth, 10);
        var height = Math.Max((int)ActualHeight, 10);
        _rendererHandle = NativeMethods.MdCreateRenderer(hwndParent.Handle, 0, 0, width, height);
        _childHwnd = NativeMethods.MdGetHwnd(_rendererHandle);
        _linkTimer.Start();
        return new HandleRef(this, _childHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _linkTimer.Stop();
        if (_rendererHandle != IntPtr.Zero)
        {
            NativeMethods.MdDestroyRenderer(_rendererHandle);
        }

        _rendererHandle = IntPtr.Zero;
        _childHwnd = IntPtr.Zero;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Resize((int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height);
    }

    public void Resize(int width, int height)
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return;
        }

        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        NativeMethods.MdResizeRenderer(_rendererHandle, width, height);
    }

    public void SetDocument(MarkdownRenderDocument document)
    {
        _linkTargets = document.LinkTargets;
        if (_rendererHandle == IntPtr.Zero)
        {
            return;
        }

        var paragraphAllocations = new List<IntPtr>(document.Paragraphs.Length * 2 + 1);
        IntPtr paragraphBuffer = IntPtr.Zero;

        try
        {
            var paragraphSize = Marshal.SizeOf<NativeMethods.MdParagraphInterop>();
            paragraphBuffer = Marshal.AllocHGlobal(paragraphSize * document.Paragraphs.Length);

            for (var i = 0; i < document.Paragraphs.Length; i++)
            {
                var p = document.Paragraphs[i];
                var paragraphInterop = new NativeMethods.MdParagraphInterop
                {
                    Text = Marshal.StringToHGlobalUni(p.Text),
                    TextLen = (uint)p.Text.Length,
                    BlockType = p.BlockType,
                    Spans = IntPtr.Zero,
                    SpanCount = (uint)p.Spans.Length
                };
                paragraphAllocations.Add(paragraphInterop.Text);

                if (p.Spans.Length > 0)
                {
                    var spanSize = Marshal.SizeOf<NativeMethods.MdSpanInterop>();
                    paragraphInterop.Spans = Marshal.AllocHGlobal(spanSize * p.Spans.Length);
                    paragraphAllocations.Add(paragraphInterop.Spans);

                    for (var s = 0; s < p.Spans.Length; s++)
                    {
                        var span = p.Spans[s];
                        var spanInterop = new NativeMethods.MdSpanInterop
                        {
                            Start = span.Start,
                            Length = span.Length,
                            StyleFlags = (uint)span.StyleFlags,
                            Rgba = span.Rgba,
                            LinkId = span.LinkId
                        };

                        var spanPtr = IntPtr.Add(paragraphInterop.Spans, s * spanSize);
                        Marshal.StructureToPtr(spanInterop, spanPtr, false);
                    }
                }

                var paraPtr = IntPtr.Add(paragraphBuffer, i * paragraphSize);
                Marshal.StructureToPtr(paragraphInterop, paraPtr, false);
            }

            NativeMethods.MdSetDocument(_rendererHandle, paragraphBuffer, (uint)document.Paragraphs.Length);
            NativeMethods.MdInvalidate(_rendererHandle);
        }
        finally
        {
            if (paragraphBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(paragraphBuffer);
            }

            foreach (var ptr in paragraphAllocations)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public void SetTheme(Theme theme)
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return;
        }

        var themeInterop = new NativeMethods.MdThemeInterop
        {
            Background = theme.Background,
            Foreground = theme.Foreground,
            Link = theme.Link,
            CodeBackground = theme.CodeBackground,
            QuoteBar = theme.QuoteBar,
            Selection = theme.Selection
        };

        NativeMethods.MdSetTheme(_rendererHandle, themeInterop);
        NativeMethods.MdInvalidate(_rendererHandle);
    }

    public void SetScrollY(float scrollY)
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.MdSetScrollY(_rendererHandle, scrollY);
        NativeMethods.MdInvalidate(_rendererHandle);
    }

    public float GetScrollY() => _rendererHandle == IntPtr.Zero ? 0 : NativeMethods.MdGetScrollY(_rendererHandle);

    public float GetContentHeight() => _rendererHandle == IntPtr.Zero ? 0 : NativeMethods.MdGetContentHeight(_rendererHandle);

    public float GetParagraphY(uint paragraphIndex)
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return 0;
        }

        return NativeMethods.MdGetParagraphY(_rendererHandle, paragraphIndex);
    }

    public HitTestResult? HitTest(float x, float y)
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return null;
        }

        if (NativeMethods.MdHitTest(_rendererHandle, x, y, out var result) == 1)
        {
            return new HitTestResult(
                result.ParagraphIndex,
                result.TextPos,
                result.Trailing,
                result.LinkId
            );
        }

        return null;
    }

    public uint? ConsumeActivatedLink()
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return null;
        }

        var linkId = NativeMethods.MdConsumeActivatedLink(_rendererHandle);
        return linkId == 0 ? null : linkId;
    }

    public void Invalidate()
    {
        if (_rendererHandle != IntPtr.Zero)
        {
            NativeMethods.MdInvalidate(_rendererHandle);
        }
    }

    public new void Dispose()
    {
        if (_rendererHandle != IntPtr.Zero)
        {
            NativeMethods.MdDestroyRenderer(_rendererHandle);
            _rendererHandle = IntPtr.Zero;
        }
        base.Dispose();
    }

    private void PollActivatedLink()
    {
        if (_rendererHandle == IntPtr.Zero)
        {
            return;
        }

        var linkId = NativeMethods.MdConsumeActivatedLink(_rendererHandle);
        if (linkId == 0)
        {
            return;
        }

        if (_linkTargets.ContainsKey(linkId))
        {
            LinkActivated?.Invoke(linkId);
        }
    }

    public bool TryResolveLink(uint linkId, out string url)
    {
        return _linkTargets.TryGetValue(linkId, out url!);
    }
}
