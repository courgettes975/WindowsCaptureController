using System.Collections.Concurrent;

namespace WindowsCaptureController.WindowsCapture;

/// <summary>
/// Per-action anti-bounce: a second press of the same action within the window is ignored
/// (one LCD key press must never open two capture overlays).
/// </summary>
public sealed class ActionDebouncer
{
    private readonly ConcurrentDictionary<string, long> _last = new();
    private readonly Func<long> _clockMs;

    public ActionDebouncer(int windowMs = 400, Func<long>? clockMs = null)
    {
        this.WindowMs = Math.Clamp(windowMs, 50, 5000);
        this._clockMs = clockMs ?? (() => Environment.TickCount64);
    }

    public int WindowMs { get; }

    /// <summary>Returns true if the action may run now (and records the time), false if it must be ignored.</summary>
    public bool TryEnter(string action)
    {
        var now = this._clockMs();
        var accepted = false;
        this._last.AddOrUpdate(
            action,
            _ => { accepted = true; return now; },
            (_, previous) =>
            {
                if (now - previous >= this.WindowMs)
                {
                    accepted = true;
                    return now;
                }

                accepted = false;
                return previous;
            });
        return accepted;
    }
}
