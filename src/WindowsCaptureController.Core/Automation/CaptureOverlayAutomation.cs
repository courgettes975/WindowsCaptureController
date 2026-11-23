using System.Diagnostics;
using System.Windows.Automation;
using WindowsCaptureController.Logging;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Automation;

/// <summary>
/// UI Automation access to the Snipping Tool capture overlay (opened by Win+Shift+S).
/// Uses AutomationIds only (see <see cref="SnippingToolUiMap"/>); never clicks at screen coordinates.
/// SAFETY: selecting the "Full screen" item inside the overlay captures immediately, so
/// <see cref="SelectMode"/> refuses it — full screen goes through Win+PrtScn instead.
/// </summary>
public static class CaptureOverlayAutomation
{
    /// <summary>Waits until an overlay toolbar is exposed by a SnippingTool process.</summary>
    public static AutomationElement? WaitForToolbarWindow(int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var w = FindToolbarWindow();
            if (w != null)
            {
                Log.Debug($"Overlay toolbar found after {sw.ElapsedMilliseconds} ms");
                return w;
            }

            Thread.Sleep(80);
        }

        return null;
    }

    public static AutomationElement? FindToolbarWindow(ISet<int>? excludeHandles = null)
    {
        foreach (var p in SnippingToolLocator.GetProcesses())
        {
            using (p)
            {
                try
                {
                    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
                        new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
                    foreach (AutomationElement w in windows)
                    {
                        if (excludeHandles != null && excludeHandles.Contains(w.Current.NativeWindowHandle))
                        {
                            continue;
                        }

                        if (ById(w, SnippingToolUiMap.OverlayToolbar) != null)
                        {
                            return w;
                        }
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
                {
                    // An overlay that is closing disappears under our feet: skip it.
                    Log.Debug($"Overlay window vanished during lookup ({ex.GetType().Name})");
                }
            }
        }

        return null;
    }

    /// <summary>Native handles of the overlay windows that exist right now (to recognise a NEW overlay).</summary>
    public static HashSet<int> CurrentToolbarHandles()
    {
        var set = new HashSet<int>();
        var w = FindToolbarWindow();
        while (w != null)
        {
            set.Add(w.Current.NativeWindowHandle);
            w = FindToolbarWindow(set);
        }

        return set;
    }

    /// <summary>Waits for an overlay that did not exist before (handles in <paramref name="before"/> are ignored).</summary>
    public static AutomationElement? WaitForNewToolbarWindow(ISet<int> before, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var w = FindToolbarWindow(before);
            if (w != null)
            {
                Log.Debug($"New overlay toolbar found after {sw.ElapsedMilliseconds} ms");
                return w;
            }

            Thread.Sleep(80);
        }

        return null;
    }

    /// <summary>True when no overlay remains within the timeout (a closing overlay can linger ~1–3 s).</summary>
    public static bool WaitUntilClosed(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (FindToolbarWindow() == null)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    public static bool IsOpen() => FindToolbarWindow() != null;

    /// <summary>Selected mode index in the overlay combo (-1 if unknown).</summary>
    public static int GetSelectedModeIndex(AutomationElement overlay)
    {
        var combo = ById(overlay, SnippingToolUiMap.SnippingModeCombo);
        if (combo == null)
        {
            return -1;
        }

        var items = Expand(combo);
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) && ((SelectionItemPattern)sp).Current.IsSelected)
                {
                    return i;
                }
            }

            return -1;
        }
        finally
        {
            Collapse(combo);
        }
    }

    /// <summary>
    /// Selects rectangle / window / freeform by index. Full screen is refused (would capture immediately).
    /// Any UI Automation error stops immediately (no retry, nothing else is touched) and returns false:
    /// the overlay then stays open in its current mode for the user.
    /// </summary>
    public static bool SelectMode(AutomationElement overlay, int index)
    {
        if (index == SnippingToolUiMap.ModeIndexFullscreen)
        {
            Log.Warning("SelectMode(full screen) refused inside the overlay: it captures immediately.");
            return false;
        }

        try
        {
            return SelectModeCore(overlay, index);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warning($"Overlay mode selection aborted ({ex.GetType().Name}: 0x{ex.HResult:X8}); mode left unchanged.");
            return false;
        }
    }

    private static bool SelectModeCore(AutomationElement overlay, int index)
    {
        var combo = ById(overlay, SnippingToolUiMap.SnippingModeCombo);
        if (combo == null)
        {
            Log.Warning($"Overlay control '{SnippingToolUiMap.SnippingModeCombo}' not found (Snipping Tool updated?).");
            return false;
        }

        // Expanded-list selection (measured reliable: 9/9 in phase 4). The collapsed alternative
        // (ItemContainer + VirtualizedItem.Realize) failed 2 times out of 6 with ElementNotAvailable: rejected.
        var items = Expand(combo);
        if (items.Count != SnippingToolUiMap.ImageModeCount || index < 0 || index >= items.Count)
        {
            Log.Warning($"Unexpected overlay mode list ({items.Count} items, expected {SnippingToolUiMap.ImageModeCount}): mode not changed.");
            Collapse(combo);
            return false;
        }

        if (items[index].TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        {
            var sel = (SelectionItemPattern)p;
            if (!sel.Current.IsSelected)
            {
                sel.Select();
            }
        }

        Collapse(combo);
        Log.Info($"Overlay mode index {index} selected via UI Automation");
        return true;
    }

    /// <summary>Closes the overlay with its own Close button (no capture taken).</summary>
    public static bool Close(AutomationElement? overlay = null)
    {
        overlay ??= FindToolbarWindow();
        var btn = overlay == null ? null : ById(overlay, SnippingToolUiMap.OverlayCloseButton);
        if (btn == null || !btn.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            return false;
        }

        ((InvokePattern)p).Invoke();
        Log.Info("Capture overlay closed via its Close button");
        return true;
    }

    internal static AutomationElement? ById(AutomationElement root, string automationId) =>
        root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

    private static List<AutomationElement> Expand(AutomationElement combo)
    {
        if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p))
        {
            ((ExpandCollapsePattern)p).Expand();
        }

        // Items are realized asynchronously after expansion.
        List<AutomationElement> items = new();
        for (var i = 0; i < 15 && items.Count < SnippingToolUiMap.ImageModeCount; i++)
        {
            Thread.Sleep(60);
            items = combo.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                .Cast<AutomationElement>().ToList();
        }

        return items;
    }

    private static void Collapse(AutomationElement combo)
    {
        try
        {
            if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var p) &&
                ((ExpandCollapsePattern)p).Current.ExpandCollapseState == ExpandCollapseState.Expanded)
            {
                ((ExpandCollapsePattern)p).Collapse();
            }
        }
        catch (ElementNotAvailableException)
        {
            // Selecting an item closes the drop-down by itself.
        }
    }
}
