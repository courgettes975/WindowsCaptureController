using System.Diagnostics;
using System.Windows.Automation;
using WindowsCaptureController.Logging;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Automation;

public enum AnnotationTool { None, Pen, Highlighter, Eraser }

/// <summary>Editor snapshot for the LCD keys (no image content).</summary>
public sealed record EditorSnapshot(bool Open, AnnotationTool Tool, bool CanUndo, bool CanRedo);

/// <summary>Stroke settings of the pen or highlighter (read from the tool flyout).</summary>
public sealed record StrokeSettings(AnnotationTool Tool, double Width, double Min, double Max, int ColorIndex, int ColorCount, string ColorName);

/// <summary>
/// UI Automation of the Snipping Tool IMAGE editor (phase 6 probe on 11.2609.15). AutomationIds only;
/// colours are chosen by INDEX (their AutomationId is the localized colour name). Flyouts are opened,
/// used and closed in the same call. The Bing visual search button is never touched.
/// </summary>
public static class EditorAutomation
{
    /// <summary>
    /// The editor window, or null if no image is open. Retries for ~1 s: right after a flyout or menu closes,
    /// the window content is briefly hidden from UI Automation (measured: one false "no image" in phase 6).
    /// </summary>
    public static AutomationElement? EditorRoot()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var root = EditorRootOnce(closeStalePopups: true);
            if (root != null)
            {
                return root;
            }

            if (SnippingToolLocator.FindMainWindow() == null)
            {
                return null; // no Snipping Tool window at all: no point waiting
            }

            Thread.Sleep(250);
        }

        return null;
    }

    /// <param name="closeStalePopups">
    /// true only for a user-requested ACTION. Passive state reads (LCD polling) must never close popups:
    /// that would close a flyout or menu the user opened by hand (measured in phase 6).
    /// </param>
    private static AutomationElement? EditorRootOnce(bool closeStalePopups)
    {
        var main = SnippingToolLocator.FindMainWindow();
        if (main == null)
        {
            return null;
        }

        try
        {
            var root = AutomationElement.FromHandle(main.Handle);
            if (CaptureOverlayAutomation.ById(root, SnippingToolUiMap.AnnotateCanvas) != null)
            {
                return root;
            }

            // Measured: while a menu popup is open, the rest of the window is hidden from UI Automation.
            // A popup left open (e.g. zoom sub-menu) is closed, then the editor is looked up again.
            if (closeStalePopups && ClosePopups(root) > 0 && CaptureOverlayAutomation.ById(root, SnippingToolUiMap.AnnotateCanvas) != null)
            {
                return root;
            }

            return null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Passive read for the LCD: one attempt, never closes anything. While a popup hides the editor,
    /// the result is "not open" for that poll only (the plugin keeps its previous display until then).</summary>
    public static EditorSnapshot Snapshot()
    {
        var root = EditorRootOnce(closeStalePopups: false);
        if (root == null)
        {
            return new EditorSnapshot(false, AnnotationTool.None, false, false);
        }

        return new EditorSnapshot(true, ActiveTool(root),
            CaptureOverlayAutomation.ById(root, SnippingToolUiMap.UndoButton)?.Current.IsEnabled == true,
            CaptureOverlayAutomation.ById(root, SnippingToolUiMap.RedoButton)?.Current.IsEnabled == true);
    }

    public static AnnotationTool ActiveTool(AutomationElement root)
    {
        foreach (var (id, tool) in Tools)
        {
            var b = CaptureOverlayAutomation.ById(root, id);
            if (b != null && b.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected)
            {
                return tool;
            }
        }

        return AnnotationTool.None;
    }

    private static readonly (string Id, AnnotationTool Tool)[] Tools =
    {
        (SnippingToolUiMap.PenButton, AnnotationTool.Pen),
        (SnippingToolUiMap.HighlighterButton, AnnotationTool.Highlighter),
        (SnippingToolUiMap.EraserButton, AnnotationTool.Eraser),
    };

    public static string ToolId(AnnotationTool tool) => Tools.First(t => t.Tool == tool).Id;

    public static bool SelectTool(AutomationElement root, AnnotationTool tool)
    {
        var b = CaptureOverlayAutomation.ById(root, ToolId(tool));
        if (b == null || !b.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        {
            return false;
        }

        ((SelectionItemPattern)p).Select();
        Log.Info($"Editor tool selected: {tool}");
        return true;
    }

    public static bool InvokeById(AutomationElement root, string id, bool requireEnabled = true)
    {
        var e = CaptureOverlayAutomation.ById(root, id);
        if (e == null || (requireEnabled && !e.Current.IsEnabled) || !e.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            return false;
        }

        ((InvokePattern)p).Invoke();
        Log.Info($"Editor: {id} invoked");
        return true;
    }

    // ---------------------------------------------------------------- tool flyout (colour / width / clear all)

    /// <summary>
    /// Opens the flyout of <paramref name="tool"/> (selecting it first), runs <paramref name="body"/>, closes it.
    /// Measured: about 1 call in 40 lost the slider between reading and setting it (flyout still rendering);
    /// such a failure is retried ONCE with a freshly opened flyout — the body re-reads the current values,
    /// so a relative change is never applied twice.
    /// </summary>
    public static T WithToolFlyout<T>(AutomationElement root, AnnotationTool tool, Func<AutomationElement, T> body)
    {
        try
        {
            return WithToolFlyoutOnce(root, tool, body);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ElementNotAvailableException)
        {
            Log.Warning($"Editor flyout ({tool}) unstable ({ex.Message}); retrying once");
            Thread.Sleep(250);
            return WithToolFlyoutOnce(root, tool, body);
        }
    }

    private static T WithToolFlyoutOnce<T>(AutomationElement root, AnnotationTool tool, Func<AutomationElement, T> body)
    {
        var b = CaptureOverlayAutomation.ById(root, ToolId(tool)) ?? throw new InvalidOperationException("tool button not found");
        if (b.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) && !((SelectionItemPattern)sp).Current.IsSelected)
        {
            ((SelectionItemPattern)sp).Select();
        }

        var ec = (ExpandCollapsePattern)b.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        ec.Expand();
        try
        {
            WaitFor(() => CaptureOverlayAutomation.ById(root, tool == AnnotationTool.Eraser ? SnippingToolUiMap.EraserClearAll : SnippingToolUiMap.StrokeWidthSlider), 1500);
            return body(root);
        }
        finally
        {
            try
            {
                if (ec.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    ec.Collapse();
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }
    }

    public static StrokeSettings? ReadStroke(AutomationElement flyoutRoot, AnnotationTool tool)
    {
        var slider = CaptureOverlayAutomation.ById(flyoutRoot, SnippingToolUiMap.StrokeWidthSlider);
        var palette = CaptureOverlayAutomation.ById(flyoutRoot, SnippingToolUiMap.ColorPalette);
        if (slider == null || palette == null)
        {
            return null;
        }

        var rv = ((RangeValuePattern)slider.GetCurrentPattern(RangeValuePattern.Pattern)).Current;
        var items = ColorItems(palette);
        var idx = items.FindIndex(i => i.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p) && ((SelectionItemPattern)p).Current.IsSelected);
        return new StrokeSettings(tool, rv.Value, rv.Minimum, rv.Maximum, idx, items.Count, idx >= 0 ? items[idx].Current.Name : "");
    }

    public static List<AutomationElement> ColorItems(AutomationElement palette) =>
        palette.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
            .Cast<AutomationElement>().ToList();

    public static bool SelectColor(AutomationElement flyoutRoot, int index)
    {
        var palette = CaptureOverlayAutomation.ById(flyoutRoot, SnippingToolUiMap.ColorPalette);
        var items = palette == null ? new List<AutomationElement>() : ColorItems(palette);
        if (index < 0 || index >= items.Count || !items[index].TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        {
            return false;
        }

        ((SelectionItemPattern)p).Select();
        return true;
    }

    /// <summary>Sets the stroke width, clamped to the slider's own range.</summary>
    public static double SetWidth(AutomationElement flyoutRoot, double value)
    {
        var slider = CaptureOverlayAutomation.ById(flyoutRoot, SnippingToolUiMap.StrokeWidthSlider) ?? throw new InvalidOperationException("width slider not found");
        var rv = (RangeValuePattern)slider.GetCurrentPattern(RangeValuePattern.Pattern);
        var v = Math.Clamp(Math.Round(value), rv.Current.Minimum, rv.Current.Maximum);
        rv.SetValue(v);
        return v;
    }

    // ---------------------------------------------------------------- overflow menu (zoom / ruler)

    /// <summary>Runs <paramref name="body"/> with the "…" overflow popup open, then closes it.</summary>
    public static T WithOverflow<T>(AutomationElement root, Func<AutomationElement, T> body)
    {
        var pop = CaptureOverlayAutomation.ById(root, SnippingToolUiMap.OverflowPopup);
        for (var attempt = 0; pop == null && attempt < 2; attempt++)
        {
            // Measured: right after another popup closed, the first press on "…" can be swallowed by the
            // closing animation. A stale popup can also hide the button. Close popups, press again.
            if (attempt > 0 || !InvokeById(root, SnippingToolUiMap.MoreOptionsButton))
            {
                ClosePopups(root);
                Thread.Sleep(150);
                if (!InvokeById(root, SnippingToolUiMap.MoreOptionsButton))
                {
                    continue;
                }
            }

            pop = WaitFor(() => CaptureOverlayAutomation.ById(root, SnippingToolUiMap.OverflowPopup), 1000);
        }

        if (pop == null)
        {
            throw new InvalidOperationException("overflow popup did not open");
        }

        try
        {
            return body(pop);
        }
        finally
        {
            CloseOverflow(root);
        }
    }

    public static void CloseOverflow(AutomationElement root) => ClosePopups(root);

    /// <summary>
    /// Closes every open XAML popup of the window (zoom sub-menu, overflow menu…), deepest first, through
    /// their WindowPattern (measured: class "Popup", WindowPattern). Returns how many were closed.
    /// </summary>
    public static int ClosePopups(AutomationElement root)
    {
        var closed = 0;
        for (var round = 0; round < 3; round++)
        {
            List<AutomationElement> popups;
            try
            {
                popups = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "Popup"))
                    .Cast<AutomationElement>().ToList();
            }
            catch (ElementNotAvailableException)
            {
                return closed;
            }

            if (popups.Count == 0)
            {
                return closed;
            }

            foreach (var pop in Enumerable.Reverse(popups))
            {
                try
                {
                    if (pop.TryGetCurrentPattern(WindowPattern.Pattern, out var wp))
                    {
                        ((WindowPattern)wp).Close();
                        closed++;
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                }
            }

            Thread.Sleep(200);
        }

        return closed;
    }

    /// <summary>Invokes a Zoom sub-menu item (ZoomInMenuItem…). Returns false if the item is disabled (limit reached).</summary>
    public static bool Zoom(AutomationElement root, string itemId) => WithOverflow(root, pop =>
    {
        // The "Zoom" entry has no AutomationId: it is the only expandable button of the overflow popup.
        var zoom = pop.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>()
            .FirstOrDefault(b => b.GetSupportedPatterns().Contains(ExpandCollapsePattern.Pattern));
        if (zoom == null)
        {
            Log.Warning("Editor zoom menu not found");
            return false;
        }

        ((ExpandCollapsePattern)zoom.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
        var item = WaitFor(() => CaptureOverlayAutomation.ById(root, itemId), 1500);
        if (item == null || !item.Current.IsEnabled)
        {
            return false;
        }

        ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Log.Info($"Editor zoom: {itemId}");
        return true;
    });

    /// <summary>Toggles the ruler; returns the button name AFTER the toggle (localized; "Masquer…" = shown).</summary>
    public static string? ToggleRuler(AutomationElement root)
    {
        var done = WithOverflow(root, pop => WaitFor(() => CaptureOverlayAutomation.ById(root, SnippingToolUiMap.RulerButton), 1500) is { } b && InvokeById(root, SnippingToolUiMap.RulerButton));
        return done ? "ok" : null;
    }

    internal static T? WaitFor<T>(Func<T?> probe, int timeoutMs) where T : class
    {
        var sw = Stopwatch.StartNew();
        do
        {
            var v = probe();
            if (v != null)
            {
                return v;
            }

            Thread.Sleep(60);
        }
        while (sw.ElapsedMilliseconds < timeoutMs);

        return null;
    }
}
