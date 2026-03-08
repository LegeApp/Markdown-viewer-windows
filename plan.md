Some previously uploaded files have expired on my side, so I can’t inspect your existing WPF shell code. That’s fine: below is a **complete, agent-oriented implementation plan** for “**WPF shell + native C++ DirectWrite renderer hosted via HwndHost**,” including **C++ component structure and code skeletons**.

---

# Goal

Implement a native rendering engine (C++ DLL) that:

* Creates a child HWND hosted inside WPF (`HwndHost`)
* Uses **DirectWrite** for text shaping/layout and **Direct2D** (or D3D-backed D2D) for drawing
* Renders Markdown content as a set of **layout paragraphs/lines**
* Supports **scroll**, **selection**, **hit-testing**, **links**, and **search highlights**
* Provides a stable API callable from C# (P/Invoke or C++/CLI)

WPF remains responsible for:

* App chrome, menus, tabs, file open, settings
* Markdown parsing (optional but recommended)
* Search UI, theme selection, recent files, etc.

---

# High-level architecture

### Process layout

* **WPF UI process** (C#)

  * Owns Markdown AST and app state
  * Hosts native renderer control via `HwndHost`
  * Pushes a “render document model” into native side (either AST-derived runs or already-tokenized spans)

* **Native renderer** (C++ DLL)

  * `RendererHostWindow` (HWND child)
  * `DeviceResources` (D3D/D2D/DWrite factories, render target)
  * `DocumentModel` (paragraphs/runs/images/blocks)
  * `LayoutEngine` (line breaking, caches)
  * `RenderEngine` (draw visible lines only, caching)
  * `InputController` (mouse/keyboard, selection, scroll)
  * `Interop API` (extern "C" exports) exposing:

    * Create/destroy renderer instance
    * Resize
    * Set document content
    * Set scroll offset
    * Hit-test and selection queries
    * Invalidate/repaint

### Data flow

1. WPF parses Markdown (Markdig or your own) → produces **render spans**:

   * paragraphs + inline runs (text, bold, italic, code, link, heading)
   * block types (paragraph, heading, list, code block, quote)
2. WPF sends those spans to native via `SetDocument(...)`.
3. Native caches:

   * paragraph layout objects
   * `IDWriteTextLayout` per paragraph (or per line for giant docs)
   * line metrics (y positions)
4. On paint, native renders **only visible lines**, plus selection, highlights.

---

# Step-by-step implementation plan (for an AI coding agent)

## Step 0 — Decisions and constraints

* Use **DirectWrite + Direct2D** (HWND render target) to start; later upgrade to D3D swap chain if needed.
* For interop, prefer **flat C ABI exports** + P/Invoke (simplest, stable).
* Document exchange format:

  * Start with **a minimal “paragraph + runs” model** (no images)
  * Keep it simple: pass UTF-16 text + style spans.

## Step 1 — Create native DLL project

* Create `mdview_native` (C++17) DLL.
* Link: `d2d1.lib`, `dwrite.lib`, `dxgi.lib` (optional), `d3d11.lib` (optional), `windowscodecs.lib` (optional for images later).
* Enable `UNICODE`.
* Add headers:

  * `windows.h`
  * `d2d1.h`, `dwrite.h`, `wrl.h`
  * `vector`, `string`, `mutex`

## Step 2 — Implement a child HWND renderer window

* Provide a class `RendererHostWindow` that:

  * Registers a window class
  * Creates a child window with `WS_CHILD | WS_VISIBLE`
  * Stores a pointer to the renderer instance in `GWLP_USERDATA`
  * Handles `WM_PAINT`, `WM_SIZE`, `WM_MOUSEWHEEL`, mouse events, keyboard focus

## Step 3 — Implement device resources

* `DeviceResources` holds:

  * `ID2D1Factory*`
  * `IDWriteFactory*`
  * `ID2D1HwndRenderTarget*`
  * Recreate render target on resize/device loss.
* Provide `BeginDraw/EndDraw`, `Resize`.

## Step 4 — Define the document model (native side)

* Minimal structs:

  * `Span { uint32_t start, length; StyleFlags flags; uint32_t color; uint32_t linkId; }`
  * `Paragraph { std::u16string text; std::vector<Span> spans; BlockType type; }`
  * `Document { std::vector<Paragraph> paragraphs; }`
* Maintain derived caches:

  * `std::vector<float> paraY;` (top y for each paragraph)
  * `std::vector<ComPtr<IDWriteTextLayout>> layouts;`
  * total height

## Step 5 — Layout engine

* For each paragraph:

  * Create `IDWriteTextFormat` per style family (normal, code, heading sizes)
  * Create `IDWriteTextLayout` for paragraph text with width = viewport width - margins
  * Apply ranges for bold/italic/color via `SetFontWeight`, `SetFontStyle`, `SetDrawingEffect` (use `ID2D1SolidColorBrush` or custom effect)
  * Compute height via `GetMetrics`
* Cache line metrics when needed for fast hit-testing:

  * `GetLineMetrics` gives line count/lengths

## Step 6 — Rendering

* On `WM_PAINT`:

  * Clear background
  * Determine visible y range: `scrollY` → `scrollY + viewportH`
  * Binary search `paraY` to find first visible paragraph
  * Draw each visible paragraph layout at `(marginLeft, paraY[i]-scrollY)`
  * Draw selection overlay and highlights after text or before (choose consistent layering)
* Selection rendering:

  * Convert selection (start/end in doc coordinates) → rects using `IDWriteTextLayout::HitTestTextRange`
  * Draw rectangles with translucent brush.

## Step 7 — Input controller + hit testing

* Convert mouse point to document coordinates: `docY = y + scrollY`
* Find paragraph by `paraY`
* Convert x/y into text position:

  * `layout->HitTestPoint(x - marginLeft, docY - paraTop, ...)` gives `textPosition`
* Manage selection start/end
* Mouse wheel updates `scrollY`, clamps `[0, totalHeight - viewportH]`, invalidates.

## Step 8 — Interop API exports

Expose C ABI functions:

* `CreateRenderer(HWND parent, int x, int y, int w, int h) -> void*`
* `DestroyRenderer(void*)`
* `ResizeRenderer(void*, int w, int h)`
* `SetScroll(void*, float scrollY)`
* `GetScroll(void*)`
* `SetDocument(void*, const ParagraphInterop*, int count)` (copy data)
* `Invalidate(void*)`
* `HitTest(void*, float x, float y, HitTestResult*)`
* `GetSelectionText(void*, wchar_t* buffer, int capacity)` (optional)

## Step 9 — WPF host (only contract-level requirements)

* WPF `HwndHost` creates the native child HWND via exported `CreateRenderer`.
* WPF sends paragraph+span arrays on document load.
* WPF passes resize events and theme changes.

## Step 10 — Perf upgrades (after correctness)

* Cache `IDWriteTextFormat`s, brushes.
* Only rebuild layouts when width/theme/doc changes.
* For huge docs:

  * use paragraph-level layout caching + LRU
  * or split very large paragraphs into chunks
* Optional: pre-render paragraphs to bitmaps (tiles) when idle.

---

# C++ code skeletons

## 1) Public interop header (C ABI)

```cpp
// mdview_native.h
#pragma once
#include <windows.h>
#include <cstdint>

#ifdef MDVIEW_NATIVE_EXPORTS
  #define MDVIEW_API extern "C" __declspec(dllexport)
#else
  #define MDVIEW_API extern "C" __declspec(dllimport)
#endif

enum MdBlockType : uint32_t {
  MdBlock_Paragraph = 0,
  MdBlock_Heading1  = 1,
  MdBlock_Heading2  = 2,
  MdBlock_CodeBlock = 3,
  MdBlock_Quote     = 4,
  MdBlock_ListItem  = 5,
};

enum MdStyleFlags : uint32_t {
  MdStyle_None   = 0,
  MdStyle_Bold   = 1 << 0,
  MdStyle_Italic = 1 << 1,
  MdStyle_Code   = 1 << 2,
  MdStyle_Link   = 1 << 3,
};

struct MdSpanInterop {
  uint32_t start;
  uint32_t length;
  uint32_t styleFlags;
  uint32_t rgba;     // 0xAARRGGBB
  uint32_t linkId;   // 0 if none
};

struct MdParagraphInterop {
  const wchar_t* text;     // UTF-16, null-terminated OR provide length
  uint32_t textLen;        // number of UTF-16 code units
  MdBlockType blockType;
  const MdSpanInterop* spans;
  uint32_t spanCount;
};

struct MdHitTestResult {
  uint32_t paragraphIndex;
  uint32_t textPos;
  uint32_t trailing; // 0/1
  uint32_t linkId;   // 0 if none
};

MDVIEW_API void* MdCreateRenderer(HWND parent, int x, int y, int w, int h);
MDVIEW_API void  MdDestroyRenderer(void* handle);
MDVIEW_API void  MdResizeRenderer(void* handle, int w, int h);
MDVIEW_API void  MdInvalidate(void* handle);

MDVIEW_API void  MdSetScrollY(void* handle, float scrollY);
MDVIEW_API float MdGetScrollY(void* handle);
MDVIEW_API float MdGetContentHeight(void* handle);

MDVIEW_API void  MdSetDocument(void* handle, const MdParagraphInterop* paras, uint32_t count);

MDVIEW_API int   MdHitTest(void* handle, float x, float y, MdHitTestResult* outResult); // returns 1 if hit
```

## 2) Renderer instance core

```cpp
// RendererInstance.h
#pragma once
#include <windows.h>
#include <wrl.h>
#include <d2d1.h>
#include <dwrite.h>
#include <vector>
#include <string>
#include <mutex>

#include "mdview_native.h"

using Microsoft::WRL::ComPtr;

struct Span {
  uint32_t start = 0;
  uint32_t length = 0;
  uint32_t styleFlags = 0;
  uint32_t rgba = 0xFFFFFFFF;
  uint32_t linkId = 0;
};

struct Paragraph {
  std::u16string text;
  MdBlockType blockType = MdBlock_Paragraph;
  std::vector<Span> spans;
};

class DeviceResources {
public:
  void Initialize(HWND hwnd);
  void Resize(UINT w, UINT h);
  ID2D1HwndRenderTarget* Target() const { return m_target.Get(); }
  IDWriteFactory* DWrite() const { return m_dwrite.Get(); }

  void BeginDraw();
  HRESULT EndDraw();
  void Clear(uint32_t rgba);

private:
  ComPtr<ID2D1Factory> m_d2dFactory;
  ComPtr<IDWriteFactory> m_dwrite;
  ComPtr<ID2D1HwndRenderTarget> m_target;
  HWND m_hwnd = nullptr;
};

class RendererInstance {
public:
  RendererInstance(HWND parent, int x, int y, int w, int h);
  ~RendererInstance();

  HWND Hwnd() const { return m_hwnd; }

  void OnResize(int w, int h);
  void OnPaint();
  void Invalidate();

  void SetScrollY(float y);
  float GetScrollY() const { return m_scrollY; }
  float GetContentHeight() const { return m_contentHeight; }

  void SetDocument(const MdParagraphInterop* paras, uint32_t count);

  bool HitTest(float x, float y, MdHitTestResult& out);

private:
  void EnsureLayouts();        // build/refresh layouts if needed
  void RebuildLayouts();       // rebuild all layouts (simple version)
  void DrawVisible();

  HWND m_hwnd = nullptr;
  DeviceResources m_dev;
  std::mutex m_mutex;

  // viewport
  int m_width = 1;
  int m_height = 1;

  // scroll
  float m_scrollY = 0.0f;

  // document + caches
  std::vector<Paragraph> m_doc;
  std::vector<float> m_paraY;
  std::vector<ComPtr<IDWriteTextLayout>> m_layouts;
  float m_contentHeight = 0.0f;

  // resources
  ComPtr<IDWriteTextFormat> m_fmtNormal;
  ComPtr<IDWriteTextFormat> m_fmtCode;
  ComPtr<IDWriteTextFormat> m_fmtH1;
  ComPtr<IDWriteTextFormat> m_fmtH2;

  // margins
  float m_marginL = 16.0f;
  float m_marginT = 16.0f;
  float m_marginR = 16.0f;
  float m_marginB = 16.0f;

  bool m_layoutDirty = true;
};
```

## 3) Child window proc + create window

```cpp
// RendererHostWindow.cpp
#include "RendererInstance.h"

static const wchar_t* kWndClass = L"MdViewNativeRendererHost";

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
  auto* inst = reinterpret_cast<RendererInstance*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));

  switch (msg) {
    case WM_NCCREATE: {
      auto cs = reinterpret_cast<CREATESTRUCT*>(lParam);
      SetWindowLongPtr(hwnd, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
      return DefWindowProc(hwnd, msg, wParam, lParam);
    }
    case WM_SIZE:
      if (inst) inst->OnResize(LOWORD(lParam), HIWORD(lParam));
      return 0;
    case WM_PAINT:
      if (inst) inst->OnPaint();
      else { PAINTSTRUCT ps; BeginPaint(hwnd, &ps); EndPaint(hwnd, &ps); }
      return 0;
    case WM_MOUSEWHEEL:
      if (inst) {
        // standard: wheel delta is 120 per notch
        int delta = GET_WHEEL_DELTA_WPARAM(wParam);
        float newY = inst->GetScrollY() - (delta / 120.0f) * 48.0f; // 48px per notch
        inst->SetScrollY(newY);
        inst->Invalidate();
      }
      return 0;
  }
  return DefWindowProc(hwnd, msg, wParam, lParam);
}

static void RegisterClassOnce() {
  static bool registered = false;
  if (registered) return;
  registered = true;

  WNDCLASS wc{};
  wc.lpfnWndProc = WndProc;
  wc.hInstance = GetModuleHandle(nullptr);
  wc.lpszClassName = kWndClass;
  wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
  RegisterClass(&wc);
}

RendererInstance::RendererInstance(HWND parent, int x, int y, int w, int h) {
  RegisterClassOnce();

  m_hwnd = CreateWindowEx(
    0, kWndClass, L"",
    WS_CHILD | WS_VISIBLE,
    x, y, w, h,
    parent, nullptr, GetModuleHandle(nullptr),
    this
  );

  m_width = (w > 0) ? w : 1;
  m_height = (h > 0) ? h : 1;

  m_dev.Initialize(m_hwnd);

  // Create text formats (font family choices can be injected from WPF)
  m_dev.DWrite()->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
    DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 16.0f, L"en-us", &m_fmtNormal);

  m_dev.DWrite()->CreateTextFormat(L"Cascadia Mono", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
    DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 15.0f, L"en-us", &m_fmtCode);

  m_dev.DWrite()->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
    DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 28.0f, L"en-us", &m_fmtH1);

  m_dev.DWrite()->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
    DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 22.0f, L"en-us", &m_fmtH2);
}

RendererInstance::~RendererInstance() {
  if (m_hwnd) DestroyWindow(m_hwnd);
}
```

## 4) DeviceResources minimal implementation

```cpp
// DeviceResources.cpp
#include "RendererInstance.h"

static D2D1_COLOR_F ColorFromRGBA(uint32_t rgba) {
  float a = ((rgba >> 24) & 0xFF) / 255.0f;
  float r = ((rgba >> 16) & 0xFF) / 255.0f;
  float g = ((rgba >> 8) & 0xFF) / 255.0f;
  float b = ((rgba >> 0) & 0xFF) / 255.0f;
  return D2D1::ColorF(r, g, b, a);
}

void DeviceResources::Initialize(HWND hwnd) {
  m_hwnd = hwnd;

  D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &m_d2dFactory);
  DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                      reinterpret_cast<IUnknown**>(m_dwrite.GetAddressOf()));

  RECT rc{};
  GetClientRect(hwnd, &rc);
  auto size = D2D1::SizeU(rc.right - rc.left, rc.bottom - rc.top);

  m_d2dFactory->CreateHwndRenderTarget(
    D2D1::RenderTargetProperties(),
    D2D1::HwndRenderTargetProperties(hwnd, size),
    &m_target
  );
}

void DeviceResources::Resize(UINT w, UINT h) {
  if (m_target) m_target->Resize(D2D1::SizeU(w, h));
}

void DeviceResources::BeginDraw() {
  m_target->BeginDraw();
}

HRESULT DeviceResources::EndDraw() {
  return m_target->EndDraw();
}

void DeviceResources::Clear(uint32_t rgba) {
  m_target->Clear(ColorFromRGBA(rgba));
}
```

## 5) Document set + layout build

```cpp
// RendererDocument.cpp
#include "RendererInstance.h"

static std::u16string CopyU16(const wchar_t* s, uint32_t len) {
  // wchar_t is UTF-16 on Windows
  return std::u16string(reinterpret_cast<const char16_t*>(s),
                        reinterpret_cast<const char16_t*>(s) + len);
}

void RendererInstance::SetDocument(const MdParagraphInterop* paras, uint32_t count) {
  std::lock_guard<std::mutex> lock(m_mutex);
  m_doc.clear();
  m_doc.reserve(count);

  for (uint32_t i = 0; i < count; i++) {
    Paragraph p;
    p.text = CopyU16(paras[i].text, paras[i].textLen);
    p.blockType = paras[i].blockType;

    p.spans.reserve(paras[i].spanCount);
    for (uint32_t j = 0; j < paras[i].spanCount; j++) {
      Span sp;
      sp.start = paras[i].spans[j].start;
      sp.length = paras[i].spans[j].length;
      sp.styleFlags = paras[i].spans[j].styleFlags;
      sp.rgba = paras[i].spans[j].rgba;
      sp.linkId = paras[i].spans[j].linkId;
      p.spans.push_back(sp);
    }

    m_doc.push_back(std::move(p));
  }

  m_layoutDirty = true;
  Invalidate();
}

void RendererInstance::EnsureLayouts() {
  if (!m_layoutDirty) return;
  RebuildLayouts();
  m_layoutDirty = false;
}

void RendererInstance::RebuildLayouts() {
  m_layouts.clear();
  m_paraY.clear();
  m_layouts.reserve(m_doc.size());
  m_paraY.reserve(m_doc.size());

  float y = m_marginT;
  float width = (float)m_width - (m_marginL + m_marginR);
  if (width < 1.0f) width = 1.0f;

  for (size_t i = 0; i < m_doc.size(); i++) {
    const auto& p = m_doc[i];

    IDWriteTextFormat* fmt = m_fmtNormal.Get();
    if (p.blockType == MdBlock_CodeBlock) fmt = m_fmtCode.Get();
    else if (p.blockType == MdBlock_Heading1) fmt = m_fmtH1.Get();
    else if (p.blockType == MdBlock_Heading2) fmt = m_fmtH2.Get();

    ComPtr<IDWriteTextLayout> layout;
    m_dev.DWrite()->CreateTextLayout(
      (const wchar_t*)p.text.c_str(),
      (UINT32)p.text.size(),
      fmt,
      width,
      100000.0f, // max height
      &layout
    );

    // Apply span styles
    for (const auto& sp : p.spans) {
      DWRITE_TEXT_RANGE range{ sp.start, sp.length };

      if (sp.styleFlags & MdStyle_Bold) {
        layout->SetFontWeight(DWRITE_FONT_WEIGHT_BOLD, range);
      }
      if (sp.styleFlags & MdStyle_Italic) {
        layout->SetFontStyle(DWRITE_FONT_STYLE_ITALIC, range);
      }
      // Color is handled via drawing effects; simplest approach:
      // store per-span, and paint via custom text renderer later.
      // For v1: ignore per-span color OR only handle link color by splitting paragraphs.
    }

    DWRITE_TEXT_METRICS metrics{};
    layout->GetMetrics(&metrics);

    m_paraY.push_back(y);
    m_layouts.push_back(layout);

    y += metrics.height + 8.0f; // paragraph spacing
  }

  m_contentHeight = y + m_marginB;
  // clamp scroll
  float maxScroll = (m_contentHeight > m_height) ? (m_contentHeight - m_height) : 0.0f;
  if (m_scrollY > maxScroll) m_scrollY = maxScroll;
}
```

> Note: Proper per-span color requires a custom `IDWriteTextRenderer` (recommended) or paragraph splitting by style runs (acceptable but messier). The agent should implement a custom text renderer in the next step.

## 6) Paint loop

```cpp
// RendererPaint.cpp
#include "RendererInstance.h"
#include <algorithm>

void RendererInstance::OnResize(int w, int h) {
  std::lock_guard<std::mutex> lock(m_mutex);
  m_width = (w > 0) ? w : 1;
  m_height = (h > 0) ? h : 1;
  m_dev.Resize((UINT)m_width, (UINT)m_height);
  m_layoutDirty = true;
  Invalidate();
}

void RendererInstance::Invalidate() {
  if (m_hwnd) InvalidateRect(m_hwnd, nullptr, FALSE);
}

void RendererInstance::OnPaint() {
  PAINTSTRUCT ps;
  BeginPaint(m_hwnd, &ps);

  {
    std::lock_guard<std::mutex> lock(m_mutex);
    EnsureLayouts();

    m_dev.BeginDraw();
    m_dev.Clear(0xFF0F0F10); // background

    DrawVisible();

    m_dev.EndDraw();
  }

  EndPaint(m_hwnd, &ps);
}

static size_t LowerBoundPara(const std::vector<float>& paraY, float y) {
  // find last para where paraY[i] <= y
  auto it = std::upper_bound(paraY.begin(), paraY.end(), y);
  if (it == paraY.begin()) return 0;
  return (size_t)((it - paraY.begin()) - 1);
}

void RendererInstance::DrawVisible() {
  auto* rt = m_dev.Target();
  if (!rt) return;

  float top = m_scrollY;
  float bottom = m_scrollY + (float)m_height;

  if (m_doc.empty()) return;

  size_t i = LowerBoundPara(m_paraY, top);
  for (; i < m_doc.size(); i++) {
    float y = m_paraY[i];
    // quick cull by paragraph top; better cull uses layout metrics
    if (y > bottom) break;

    float drawY = y - m_scrollY;
    float drawX = m_marginL;

    // Simple draw:
    rt->DrawTextLayout(
      D2D1::Point2F(drawX, drawY),
      m_layouts[i].Get(),
      nullptr // brush ignored by DrawTextLayout when no custom renderer; needs brush overload?
    );

    // In practice: use a brush:
    ComPtr<ID2D1SolidColorBrush> brush;
    rt->CreateSolidColorBrush(D2D1::ColorF(1,1,1,1), &brush);
    rt->DrawTextLayout(D2D1::Point2F(drawX, drawY), m_layouts[i].Get(), brush.Get());
  }
}

void RendererInstance::SetScrollY(float y) {
  float maxScroll = (m_contentHeight > m_height) ? (m_contentHeight - m_height) : 0.0f;
  if (y < 0.0f) y = 0.0f;
  if (y > maxScroll) y = maxScroll;
  m_scrollY = y;
}
```

## 7) Hit testing (paragraph + layout point)

```cpp
// RendererHitTest.cpp
#include "RendererInstance.h"

bool RendererInstance::HitTest(float x, float y, MdHitTestResult& out) {
  std::lock_guard<std::mutex> lock(m_mutex);
  EnsureLayouts();

  float docY = y + m_scrollY;
  if (m_doc.empty()) return false;

  // find paragraph
  size_t pi = LowerBoundPara(m_paraY, docY);
  if (pi >= m_doc.size()) return false;

  float paraTop = m_paraY[pi];
  float localY = docY - paraTop;
  float localX = x - m_marginL;
  if (localX < 0) localX = 0;

  BOOL isTrailing = FALSE, isInside = FALSE;
  DWRITE_HIT_TEST_METRICS m{};
  HRESULT hr = m_layouts[pi]->HitTestPoint(localX, localY, &isTrailing, &isInside, &m);
  if (FAILED(hr) || !isInside) return false;

  out.paragraphIndex = (uint32_t)pi;
  out.textPos = m.textPosition;
  out.trailing = isTrailing ? 1u : 0u;
  out.linkId = 0; // resolve by checking spans containing textPos
  for (const auto& sp : m_doc[pi].spans) {
    if (sp.linkId != 0 && out.textPos >= sp.start && out.textPos < (sp.start + sp.length)) {
      out.linkId = sp.linkId;
      break;
    }
  }
  return true;
}
```

## 8) Export implementation

```cpp
// exports.cpp
#include "RendererInstance.h"

MDVIEW_API void* MdCreateRenderer(HWND parent, int x, int y, int w, int h) {
  try {
    auto* inst = new RendererInstance(parent, x, y, w, h);
    return inst;
  } catch (...) {
    return nullptr;
  }
}

MDVIEW_API void MdDestroyRenderer(void* handle) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  delete inst;
}

MDVIEW_API void MdResizeRenderer(void* handle, int w, int h) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  if (inst) inst->OnResize(w, h);
}

MDVIEW_API void MdInvalidate(void* handle) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  if (inst) inst->Invalidate();
}

MDVIEW_API void MdSetScrollY(void* handle, float scrollY) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  if (inst) inst->SetScrollY(scrollY);
}

MDVIEW_API float MdGetScrollY(void* handle) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  return inst ? inst->GetScrollY() : 0.0f;
}

MDVIEW_API float MdGetContentHeight(void* handle) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  return inst ? inst->GetContentHeight() : 0.0f;
}

MDVIEW_API void MdSetDocument(void* handle, const MdParagraphInterop* paras, uint32_t count) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  if (inst) inst->SetDocument(paras, count);
}

MDVIEW_API int MdHitTest(void* handle, float x, float y, MdHitTestResult* outResult) {
  auto* inst = reinterpret_cast<RendererInstance*>(handle);
  if (!inst || !outResult) return 0;
  return inst->HitTest(x, y, *outResult) ? 1 : 0;
}
```

---

# Required “next” work items the agent must implement (to make it real)

### 1) Proper span coloring + link styling

Implement a custom `IDWriteTextRenderer` so you can apply per-range brushes/effects cleanly.

* This is the “correct” way to do colored spans, selection, underline, etc.
* Agent task: implement:

  * `class TextRenderer : public IDWriteTextRenderer`
  * Use `ID2D1RenderTarget` to draw glyph runs with the correct brush per run

### 2) Selection model

Add:

* Document-global selection anchor: `{paraIndex, textPos}`
* Mouse drag updates selection range
* Render selection rects via `HitTestTextRange`

### 3) Virtualization for very large docs

If doc > N paragraphs:

* Keep a cache of `TextLayout` objects in an LRU keyed by paragraph index + width + theme version
* Only layout visible window + buffer

### 4) Threading (optional)

* Keep all D2D/DWrite calls on UI thread (HWND thread).
* You can parse markdown and build paragraph model off-thread in C#, then send over.

---

# Minimal WPF-side contract (agent should implement, but you said “focus C++”)

The agent must ensure WPF does:

* Defines `HwndHost` that calls `MdCreateRenderer(parentHwnd, 0,0,w,h)`
* On size changed: `MdResizeRenderer(handle, w, h)`
* On doc load: build `MdParagraphInterop[]` and pin memory, call `MdSetDocument`
* Forward mouse wheel if needed (often native child gets it directly)

---

If you want, I can also provide the **exact C# P/Invoke structs + HwndHost implementation** (about ~150–250 lines) that matches the exported ABI above—without needing any of your existing WPF shell code.
