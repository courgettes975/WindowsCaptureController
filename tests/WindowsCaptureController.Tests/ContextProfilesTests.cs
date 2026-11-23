using WindowsCaptureController.Shared;
using WindowsCaptureController.WindowsCapture;

namespace WindowsCaptureController.Tests;

public class ContextProfilesTests
{
    public static IEnumerable<object[]> States() =>
        Enum.GetNames<CaptureAppState>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(States))]
    public void Every_state_has_nine_slots_and_only_known_actions(string state)
    {
        var p = ContextProfiles.For(state);
        Assert.Equal(ContextProfiles.Slots, p.Count);
        Assert.All(p.Where(id => id.Length > 0), id => Assert.True(ContextProfiles.Actions.ContainsKey(id), $"unknown action {id}"));
    }

    [Fact]
    public void Data_losing_actions_require_confirmation()
    {
        Assert.True(ContextProfiles.Actions["rec.discard"].Confirm);
    }

    [Fact]
    public void Recording_controls_only_appear_while_recording()
    {
        foreach (var state in Enum.GetNames<CaptureAppState>().Where(s => s is not ("Recording" or "RecordingPaused")))
        {
            Assert.DoesNotContain("rec.stop", ContextProfiles.For(state));
            Assert.DoesNotContain("rec.pause", ContextProfiles.For(state));
            Assert.DoesNotContain("rec.discard", ContextProfiles.For(state));
        }

        Assert.Contains("rec.stop", ContextProfiles.For("Recording"));
    }

    [Fact]
    public void Start_recording_is_offered_only_once_the_area_is_drawn()
    {
        foreach (var state in Enum.GetNames<CaptureAppState>().Where(s => s != "VideoReady"))
        {
            Assert.DoesNotContain("rec.start", ContextProfiles.For(state));
        }
    }

    [Fact]
    public void Disabled_profiles_always_use_the_capture_page()
    {
        Assert.Equal(ContextProfiles.For("Closed"), ContextProfiles.For("ImageEditing", profilesEnabled: false));
    }

    [Theory]
    [InlineData("ImageEditing", 1, "pen")]
    [InlineData("Recording", 1, "rec.stop")]
    [InlineData("Closed", 9, "open")]
    [InlineData("VideoReady", 4, null)]
    [InlineData("Closed", 0, null)]
    [InlineData("Closed", 10, null)]
    public void Resolve_maps_slots(string state, int slot, string? expected)
    {
        Assert.Equal(expected, ContextProfiles.Resolve(state, slot)?.Id);
    }

    [Fact]
    public void Display_only_actions_have_no_command()
    {
        Assert.All(ContextProfiles.Actions.Values.Where(a => a.DisplayOnly), a => Assert.Null(a.Command));
    }
}
