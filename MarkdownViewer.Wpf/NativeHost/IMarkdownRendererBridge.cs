using MarkdownViewer.Wpf.Models;
using MarkdownViewer.Wpf.Services;
using System;

namespace MarkdownViewer.Wpf.NativeHost;

public interface IMarkdownRendererBridge : IDisposable
{
    event Action<uint>? LinkActivated;
    
    IntPtr Handle { get; }
    
    void Resize(int width, int height);
    void SetDocument(MarkdownRenderDocument document);
    void SetScrollY(float scrollY);
    float GetScrollY();
    float GetContentHeight();
    float GetParagraphY(uint paragraphIndex);
    void SetTheme(Theme theme);
    HitTestResult? HitTest(float x, float y);
    uint? ConsumeActivatedLink();
    void Invalidate();
}
