namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System.Collections.Generic;

    // Approximate RGB of the Snipping Tool ink colours, keyed by the names measured in the phase 6 probe
    // (fr-FR palette: 30 pen colours, 6 highlighter colours). Only used to tint the LCD icon; the colour
    // itself is always chosen in the Snipping Tool by INDEX. Unknown name (other language) -> neutral white.
    internal static class InkColors
    {
        private static readonly Dictionary<string, BitmapColor> Map = new()
        {
            ["Noir"] = new(20, 20, 20), ["Blanc"] = new(255, 255, 255), ["Gris argent"] = new(192, 192, 192),
            ["Gris"] = new(128, 128, 128), ["Gris foncé"] = new(80, 80, 80), ["Magenta"] = new(255, 0, 255),
            ["Rouge"] = new(232, 17, 35), ["Rouge orangé"] = new(255, 69, 0), ["Orange"] = new(255, 165, 0),
            ["Jaune or"] = new(255, 200, 0), ["Jaune"] = new(255, 240, 0), ["Vert gazon"] = new(124, 220, 0),
            ["Vert"] = new(0, 160, 60), ["Vert foncé"] = new(0, 100, 0), ["Bleu-vert"] = new(0, 140, 140),
            ["Bleu"] = new(0, 90, 255), ["Bleu indigo"] = new(75, 0, 130), ["Mauve"] = new(190, 130, 230),
            ["Violet"] = new(128, 0, 128), ["Beige"] = new(235, 225, 195), ["Marron clair"] = new(210, 180, 140),
            ["Brun"] = new(140, 70, 30), ["Marron foncé"] = new(92, 64, 51), ["Rose pastel"] = new(255, 200, 215),
            ["Orange pastel"] = new(255, 215, 175), ["Jaune pastel"] = new(255, 250, 180), ["Vert pastel"] = new(195, 240, 195),
            ["Bleu pastel"] = new(175, 200, 230), ["Violet pastel"] = new(205, 195, 230),
            ["Bleu clair"] = new(0, 220, 255), ["Rose"] = new(255, 105, 180),
        };

        public static BitmapColor? Of(string name) =>
            !string.IsNullOrEmpty(name) && Map.TryGetValue(name, out var c) ? c : null;
    }
}
