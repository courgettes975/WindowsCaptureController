using WindowsCaptureController.Shared;

namespace WindowsCaptureController.Tests;

public class DialAccumulatorTests
{
    [Fact]
    public void Slow_rotation_gives_one_step_per_tick()
    {
        var a = new DialAccumulator();
        Assert.True(a.Add(1));
        Assert.Equal(1, a.TakeSteps());
        Assert.Equal(0, a.TakeSteps());   // idle again
        Assert.True(a.Add(-1));
        Assert.Equal(-1, a.TakeSteps());
    }

    [Fact]
    public void Ticks_during_a_command_are_accumulated_not_restarted()
    {
        var a = new DialAccumulator();
        Assert.True(a.Add(1));
        Assert.False(a.Add(1));   // in flight: caller must not start another flush
        Assert.False(a.Add(1));
        Assert.Equal(3, a.TakeSteps());
        Assert.Equal(0, a.TakeSteps());
    }

    [Fact]
    public void Fast_spin_is_capped_and_excess_dropped()
    {
        var a = new DialAccumulator(maxStepsPerFlush: 4);
        a.Add(30);
        Assert.Equal(4, a.TakeSteps());
        Assert.Equal(26, a.DroppedSteps);
        Assert.Equal(0, a.TakeSteps());   // no backlog keeps zooming after the user stopped
    }

    [Fact]
    public void Sensitivity_keeps_the_remainder()
    {
        var a = new DialAccumulator(sensitivity: 3);
        a.Add(2);
        Assert.Equal(0, a.TakeSteps());   // 2 ticks < 3
        a.Add(2);
        Assert.Equal(1, a.TakeSteps());   // 4 ticks -> 1 step, 1 tick left
        a.Add(2);
        Assert.Equal(1, a.TakeSteps());   // 1 + 2 = 3 ticks -> 1 step
    }

    [Fact]
    public void Inverted_direction_and_opposite_turns_cancel()
    {
        var a = new DialAccumulator(inverted: true);
        a.Add(2);
        a.Add(-1);
        Assert.Equal(-1, a.TakeSteps());
    }

    [Fact]
    public void Clear_forgets_pending_ticks()
    {
        var a = new DialAccumulator(sensitivity: 2);
        a.Add(1);
        a.Clear();
        a.Add(1);
        Assert.Equal(0, a.TakeSteps());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 10)]
    public void Sensitivity_is_clamped(int requested, int expected)
    {
        Assert.Equal(expected, new DialAccumulator(requested).Sensitivity);
    }
}
