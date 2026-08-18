---
name: winui3-responsive-design
description: "Web-inspired responsive design rules and layout patterns for WinUI 3 desktop and kiosk applications (portrait, landscape, multi-resolution scaling, zero-clipping)."
---

# WinUI 3 Responsive Design (Web-to-XAML Blueprint)

This skill translates modern **Web Responsive Design** concepts (CSS Flexbox, CSS Grid, Media Queries, Container Queries, Clamp/Fluid sizing) into **WinUI 3 (XAML / C#)** architecture.

---

## 1. Web to WinUI 3 Concept Mapping

| Web / CSS Concept | WinUI 3 Equivalent | Implementation Rule |
| :--- | :--- | :--- |
| **Media Queries (`@media (min-width: 1100px)`)** | `VisualStateManager` + `<AdaptiveTrigger MinWindowWidth="1100"/>` | Place in root container of every Page or UserControl. |
| **Portrait vs Landscape Queries** | `AdaptiveTrigger MinWindowHeight="..."` or `MinWindowWidth` | Breakpoints for 21.5" & 27.5" portrait vs 16:9 widescreen. |
| **CSS Flexbox (`flex-direction: row / column`)** | `Grid` with dynamic `RowDefinitions` / `ColumnDefinitions` OR switching `TwoColumnGrid` vs `SingleColumnStack` | Toggle visibility or row/column indices via `VisualState.Setters`. |
| **Flex Wrap (`flex-wrap: wrap`)** | `VariableSizedWrapGrid` or `ItemsControl` with Wrap panel | Never use horizontal `StackPanel` for items that could exceed container width. |
| **CSS Grid `repeat(auto-fit, minmax(...))`** | Adaptive `VariableSizedWrapGrid MaximumRowsOrColumns` or `UniformGrid` | Adjust columns from 2/3 on vertical to 4/5 on widescreen. |
| **`max-width: 1200px; margin: 0 auto;`** | `HorizontalAlignment="Center" MaxWidth="1180"` | Prevents layouts from stretching infinitely on 4K/wide screens. |
| **`clamp()`, `min()`, `max()` / Fluid text** | `MinHeight`, `MaxHeight`, `MinWidth`, `MaxWidth`, `TextTrimming="CharacterEllipsis"` | Constrain child boundaries so parent scrolling handles overflow. |
| **`overflow-y: auto; overflow-x: hidden;`** | `<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">` | Always disable horizontal scrolling to prevent awkward sideways scrollbars on touch kiosks. |

---

## 2. Standard Breakpoint Matrix

| Screen Target | Aspect Ratio | Resolution | WinUI 3 Adaptive Trigger |
| :--- | :--- | :--- | :--- |
| **Narrow / Compact Window** | Variable | Width < 600px | `<AdaptiveTrigger MinWindowWidth="0" MinWindowHeight="0"/>` |
| **Vertical Kiosk (21.5" & 27.5")** | 9:16 Portrait | 1080x1920 / 1440x2560 | `<AdaptiveTrigger MinWindowWidth="0" MinWindowHeight="1000"/>` |
| **Horizontal Widescreen** | 16:9 Landscape | 1920x1080 / 2560x1440 | `<AdaptiveTrigger MinWindowWidth="1100" MinWindowHeight="0"/>` |

---

## 3. Core Anti-Patterns & Rules

1. **NO Hardcoded Fixed Pixel Widths on Root Elements**:
   - ❌ BAD: `<StackPanel Width="1000">`
   - ✅ GOOD: `<StackPanel MaxWidth="1120" HorizontalAlignment="Center">`
2. **NO Overflowing Horizontal StackPanels without Wrapping**:
   - ❌ BAD: `<StackPanel Orientation="Horizontal" Spacing="32">` (3+ cards inside will clip on narrow screens).
   - ✅ GOOD: Stack items vertically on portrait or use a flexible wrap grid.
3. **Always Set Explicit VisualState Setters for Contrast on Hover**:
   - Ensure `ButtonBackgroundPointerOver` and `ButtonForegroundPointerOver` keep high contrast so text never washes out to white on light backgrounds.
4. **ContentDialog Centering**:
   - In WinUI 3, keep `ContentDialog` horizontal/vertical alignment at `Stretch` and set width constraints on the *inner Content Border*.
