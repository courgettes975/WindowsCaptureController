using WindowsCaptureController.Automation;

namespace WindowsCaptureController.Tests;

/// <summary>
/// The recorder's mic / system-audio buttons have no Toggle pattern: state is parsed from their name.
/// Strings below are the EXACT fr-FR names measured in the phase 5 probe.
/// </summary>
public class RecordingNameTests
{
    [Theory]
    [InlineData("Micro désactivé. Sélectionnez cette option pour activer le micro.", true, TriState.Off)]
    [InlineData("Désactiver le son", false, TriState.On)]
    [InlineData("Activer le son", false, TriState.Off)]
    [InlineData("Something unexpected", true, TriState.Unknown)]
    public void Microphone_state_from_measured_names(string name, bool recordingToolbar, TriState expected)
    {
        Assert.Equal(expected, RecordingAutomation.ParseMicName(name, recordingToolbar));
    }

    [Theory]
    [InlineData("Audio système activé. Sélectionnez cette option pour désactiver l’audio système.", TriState.On)]
    [InlineData("Audio système désactivé. Sélectionnez cette option pour activer l’audio système.", TriState.Off)]
    [InlineData("???", TriState.Unknown)]
    public void System_audio_state_reads_only_the_first_sentence(string name, TriState expected)
    {
        Assert.Equal(expected, RecordingAutomation.ParseSystemAudioName(name));
    }

    [Theory]
    [InlineData("Suspendre l’enregistrement", TriState.Off)]          // measured: running
    [InlineData("Enregistrement des résumés", TriState.On)]           // measured: paused (sic)
    [InlineData("Pause recording", TriState.Off)]
    [InlineData("Resume recording", TriState.On)]
    [InlineData("", TriState.Unknown)]
    public void Paused_state_from_pause_button_name(string name, TriState paused)
    {
        Assert.Equal(paused, RecordingAutomation.ParsePausedName(name));
    }

    [Fact]
    public void Discard_without_confirmation_is_refused()
    {
        var r = new WindowsCaptureController.WindowsCapture.WindowsCaptureController().DiscardRecording(confirmed: false);
        Assert.Equal(WindowsCaptureController.WindowsCapture.ActionStatus.Failed, r.Status);
    }
}
