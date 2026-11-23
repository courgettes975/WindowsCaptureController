#nullable enable
using System;

namespace WindowsCaptureController.Shared;

/// <summary>
/// Turns raw dial ticks into a bounded number of steps without flooding the Snipping Tool:
///  * sensitivity N = N ticks per step (1 = every tick); leftover ticks are kept for the next turn;
///  * direction can be inverted;
///  * while one command is in flight, further ticks are only accumulated (one command at a time);
///  * a flush returns at most <see cref="MaxStepsPerFlush"/> steps; the rest is DROPPED (a fast spin must not
///    leave a backlog of zoom steps that keeps running after the user stopped turning).
/// Thread-safe. Pure logic (unit tested, shared by the plugin and the tests).
/// </summary>
public sealed class DialAccumulator
{
    private readonly object _gate = new();
    private int _ticks;
    private bool _inFlight;

    public DialAccumulator(int sensitivity = 1, bool inverted = false, int maxStepsPerFlush = 4)
    {
        this.Sensitivity = Math.Clamp(sensitivity, 1, 10);
        this.Inverted = inverted;
        this.MaxStepsPerFlush = Math.Max(1, maxStepsPerFlush);
    }

    public int Sensitivity { get; set; }

    public bool Inverted { get; set; }

    public int MaxStepsPerFlush { get; }

    /// <summary>Total steps dropped because the dial was spun faster than the target can follow.</summary>
    public int DroppedSteps { get; private set; }

    /// <summary>Adds ticks. Returns true when the caller should start a flush (nothing in flight).</summary>
    public bool Add(int diff)
    {
        lock (this._gate)
        {
            this._ticks += this.Inverted ? -diff : diff;
            if (this._inFlight)
            {
                return false;
            }

            this._inFlight = true;
            return true;
        }
    }

    /// <summary>
    /// Takes the pending steps (signed, bounded). When it returns 0 the flush loop must stop:
    /// the accumulator is then idle again (next Add() returns true).
    /// </summary>
    public int TakeSteps()
    {
        lock (this._gate)
        {
            var steps = this._ticks / this.Sensitivity;        // truncates toward zero
            this._ticks -= steps * this.Sensitivity;            // keep the remainder (same sign)
            if (steps == 0)
            {
                this._inFlight = false;
                return 0;
            }

            if (Math.Abs(steps) > this.MaxStepsPerFlush)
            {
                this.DroppedSteps += Math.Abs(steps) - this.MaxStepsPerFlush;
                steps = Math.Sign(steps) * this.MaxStepsPerFlush;
            }

            return steps;
        }
    }

    /// <summary>Forgets pending ticks (mode change, reset press).</summary>
    public void Clear()
    {
        lock (this._gate)
        {
            this._ticks = 0;
        }
    }
}
