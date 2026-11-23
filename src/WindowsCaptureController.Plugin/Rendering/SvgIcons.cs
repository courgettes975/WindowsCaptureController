namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.IO;
    using System.Reflection;

    // SVG icon sources (assets\icons\*.svg, embedded as "icons.<Glyph>.svg") rendered with the documented
    // PluginApi call BitmapImage.FromSvg(xml). Colour tokens FG / ACCENT / BG and the stroke token SW are
    // replaced per state. Results are cached (a key image is redrawn often: countdown, timer, polling).
    // Any failure (missing resource, invalid SVG) returns null: Icons.Render then falls back to the
    // vector drawing in Icons.DrawGlyph, so a key is never left blank.
    internal static class SvgIcons
    {
        private static readonly ConcurrentDictionary<string, string> Sources = new();
        private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
        private static readonly ConcurrentDictionary<string, bool> Failed = new();

        public static int CacheCount => Cache.Count;

        public static BitmapImage TryGet(Glyph glyph, BitmapColor fg, BitmapColor accent, BitmapColor bg, float strokeWidth)
        {
            if (glyph == Glyph.Empty)
            {
                return null;
            }

            var name = glyph.ToString();
            var key = $"{name}|{fg.ARGB:X8}|{accent.ARGB:X8}|{bg.ARGB:X8}|{(glyph == Glyph.Width ? strokeWidth : 0):0}";
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (Failed.ContainsKey(name))
            {
                return null;
            }

            try
            {
                var src = Sources.GetOrAdd(name, Load);
                if (string.IsNullOrEmpty(src))
                {
                    Failed[name] = true;
                    return null;
                }

                // Width glyph: SW = 3..40 user-space units for 1..64 px of real stroke.
                var sw = Math.Clamp(3f + strokeWidth * 37f / 64f, 3f, 40f).ToString("0.#", CultureInfo.InvariantCulture);
                var xml = src.Replace("ACCENT", Hex(accent)).Replace("FG", Hex(fg)).Replace("BG", Hex(bg)).Replace("'SW'", $"'{sw}'");
                if (!BitmapImage.TryCreateFromSvg(xml, out var img) || img == null)
                {
                    Failed[name] = true;
                    PluginLog.Warning($"SVG icon '{name}' could not be rendered: drawn fallback used");
                    return null;
                }

                if (Cache.Count > 600)
                {
                    Cache.Clear(); // bounded memory (states x colours)
                }

                Cache[key] = img;
                return img;
            }
            catch (Exception ex)
            {
                Failed[name] = true;
                PluginLog.Warning($"SVG icon '{name}' failed: drawn fallback used", ex);
                return null;
            }
        }

        /// <summary>Renders every glyph once at load: logs how many SVG sources the installed SDK accepted.</summary>
        public static (int Ok, int Total) SelfTest()
        {
            var ok = 0;
            var total = 0;
            foreach (Glyph g in Enum.GetValues(typeof(Glyph)))
            {
                if (g == Glyph.Empty)
                {
                    continue;
                }

                total++;
                if (TryGet(g, Palette.White, Palette.Red, Palette.Background, 4) != null)
                {
                    ok++;
                }
            }

            return (ok, total);
        }

        private static string Load(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"icons.{name}.svg");
            if (s == null)
            {
                return "";
            }

            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        private static string Hex(BitmapColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
