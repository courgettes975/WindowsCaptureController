namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Threading.Tasks;

    // Keypad key "Mode molette": a press cycles Zoom -> Épaisseur -> Couleur -> Historique -> Délai.
    // Its LCD shows the current mode and the last value produced by the dial (the Dialpad has no screen).
    public sealed class DialModeKey : PluginDynamicCommand
    {
        private const string Id = "wcc.dial.mode";

        public DialModeKey()
            : base()
        {
            this.AddParameter(Id, "Mode molette (zoom / épaisseur / couleur / historique / délai)", "Windows Capture - Molette");
        }

        private DialState State => (this.Plugin as WindowsCaptureControllerPlugin)?.Dial;

        protected override bool OnLoad()
        {
            if (this.State != null)
            {
                this.State.Changed += this.Refresh;
            }

            return true;
        }

        protected override bool OnUnload()
        {
            if (this.State != null)
            {
                this.State.Changed -= this.Refresh;
            }

            return true;
        }

        private void Refresh() => this.ActionImageChanged(Id);

        protected override void RunCommand(string actionParameter)
        {
            var plugin = this.Plugin as WindowsCaptureControllerPlugin;
            var state = this.State;
            if (state == null)
            {
                return;
            }

            var mode = state.NextMode();
            PluginLog.Info($"Dial mode -> {mode}");
            Task.Run(() => plugin?.RefreshDialSettings()); // picks up config.json edits (direction / sensitivity)
        }

        protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
        {
            var state = this.State;
            var mode = state?.Mode ?? "zoom";
            var glyph = mode switch
            {
                "zoom" => Glyph.ZoomIn,
                "thickness" => Glyph.Width,
                "color" => Glyph.Swatch,
                "history" => Glyph.LastCapture,
                "delay" => Glyph.Delay,
                _ => Glyph.Empty,
            };
            var status = state?.Status(mode) ?? "";
            return Icons.Render(imageSize, new Icons.Spec
            {
                Glyph = glyph,
                Label = string.IsNullOrEmpty(status) ? "◎ " + DialState.Label(mode) : status,
                Corner = DialState.Label(mode),
                Frame = Palette.Blue,
                Accent = mode == "color" ? InkColors.Of(status.TrimEnd(' ', '✓')) : null,
            });
        }

        protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => "Mode molette";
    }
}
