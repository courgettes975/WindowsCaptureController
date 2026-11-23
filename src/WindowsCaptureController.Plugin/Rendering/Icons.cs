namespace Loupedeck.WindowsCaptureControllerPlugin
{
    using System;

    public enum Glyph
    {
        Scissors,
        NewCapture,
        Rectangle,
        Window,
        Freeform,
        Fullscreen,
        Folder,
        Delay,
        Cancel,
        LastCapture,
        VideoCamera,
        VideoFolder,
        LastVideo,
        RecordDot,
        StopSquare,
        PauseBars,
        Microphone,
        Trash,
        Pen,
        Highlighter,
        Eraser,
        Undo,
        Redo,
        Copy,
        Save,
        Share,
        Ruler,
        ZoomIn,
        ZoomOut,
        Zoom100,
        ZoomFit,
        Swatch,
        Width,
        Play,
        MicrophoneOff,
        Text,
        Redact,
        Settings,
        Diagnostic,
        Error,
        Success,
        Empty,
    }

    internal static class Palette
    {
        public static readonly BitmapColor Background = new(18, 20, 24);
        public static readonly BitmapColor Text = new(235, 238, 242);
        public static readonly BitmapColor Grey = new(95, 100, 110);
        public static readonly BitmapColor Blue = new(0, 103, 192);
        public static readonly BitmapColor DarkGrey = new(48, 52, 60);
        public static readonly BitmapColor Green = new(46, 204, 113);
        public static readonly BitmapColor Orange = new(245, 160, 40);
        public static readonly BitmapColor Red = new(231, 60, 60);
        public static readonly BitmapColor White = new(255, 255, 255);
        public static readonly BitmapColor FolderYellow = new(240, 190, 70);
        public static readonly BitmapColor DarkRed = new(120, 18, 18);
    }

    // Every key image is drawn at runtime with BitmapBuilder primitives: crisp at every LCD size,
    // high contrast, no third-party artwork (no licence question). Glyph in the top ~70 %, label below.
    internal static class Icons
    {
        public sealed class Spec
        {
            public Glyph Glyph;
            public string Label = "";
            public BitmapColor Fg = Palette.White;
            public BitmapColor Bg = Palette.Background;
            public BitmapColor? Frame;

            /// <summary>Glyph accent colour (pen tip, swatch fill).</summary>
            public BitmapColor? Accent;

            /// <summary>Stroke width shown by the Width glyph (1..64).</summary>
            public float WidthValue = 4;

            /// <summary>When set, drawn huge instead of the glyph (countdown digits).</summary>
            public string BigText;
            public BitmapColor BigTextColor = Palette.White;
            public string Corner = "";   // small text in the top-right corner (e.g. "3 s")
        }

        public static BitmapImage Render(PluginImageSize size, Spec spec)
        {
            using var b = new BitmapBuilder(size);
            var w = b.Width;
            var h = b.Height;
            var s = Math.Min(w, h);
            b.Clear(spec.Bg);

            if (spec.Frame.HasValue)
            {
                var t = Math.Max(3, (int)(s * 0.05f));
                b.FillRectangle(0, 0, w, t, spec.Frame.Value);
                b.FillRectangle(0, h - t, w, t, spec.Frame.Value);
                b.FillRectangle(0, 0, t, h, spec.Frame.Value);
                b.FillRectangle(w - t, 0, t, h, spec.Frame.Value);
            }

            var hasLabel = !string.IsNullOrEmpty(spec.Label);
            var cx = w / 2f;
            var cy = hasLabel ? h * 0.40f : h * 0.5f;
            var r = s * (hasLabel ? 0.26f : 0.32f);
            if (!string.IsNullOrEmpty(spec.BigText))
            {
                b.DrawText(spec.BigText, 0, 0, w, (int)(h * (hasLabel ? 0.74f : 1f)), spec.BigTextColor, (int)(s * 0.5f));
            }
            else
            {
                // Source of truth: assets\icons\<Glyph>.svg (BitmapImage.FromSvg). Drawn fallback otherwise.
                var svg = SvgIcons.TryGet(spec.Glyph, spec.Fg, spec.Accent ?? Palette.Red, spec.Bg, spec.WidthValue);
                if (svg != null)
                {
                    var side = (int)(r * 2.3f);
                    b.DrawImage(svg, (int)(cx - side / 2f), (int)(cy - side / 2f), side, side);
                }
                else
                {
                    Accent = spec.Accent;
                    WidthValue = spec.WidthValue;
                    DrawGlyph(b, spec.Glyph, cx, cy, r, spec.Fg, spec.Bg);
                }
            }

            if (!string.IsNullOrEmpty(spec.Corner))
            {
                b.DrawText(spec.Corner, (int)(w * 0.52f), (int)(h * 0.02f), (int)(w * 0.46f), (int)(h * 0.2f), Palette.Orange, Math.Max(8, (int)(s * 0.13f)));
            }

            if (hasLabel)
            {
                b.DrawText(spec.Label, 0, (int)(h * 0.72f), w, (int)(h * 0.24f), Palette.Text, FitFont(spec.Label, s));
            }

            return b.ToImage();
        }

        [ThreadStatic] private static BitmapColor? Accent;
        [ThreadStatic] private static float WidthValue;

        private static void Arrow(BitmapBuilder b, float x1, float y1, float x2, float y2, BitmapColor c, float t, float head)
        {
            b.DrawLine(x1, y1, x2, y2, c, t);
            var a = Math.Atan2(y2 - y1, x2 - x1);
            for (var k = -1; k <= 1; k += 2)
            {
                var aa = a + Math.PI + k * 0.55;
                b.DrawLine(x2, y2, x2 + (float)(Math.Cos(aa) * head), y2 + (float)(Math.Sin(aa) * head), c, t);
            }
        }

        private static void Magnifier(BitmapBuilder b, float cx, float cy, float r, BitmapColor fg, BitmapColor bg, float t)
        {
            Ring(b, cx - r * 0.2f, cy - r * 0.2f, r * 0.62f, fg, bg, t);
            b.DrawLine(cx + r * 0.25f, cy + r * 0.25f, cx + r * 0.85f, cy + r * 0.85f, fg, t * 1.4f);
        }

        private static int FitFont(string text, int s)
        {
            var size = s * 0.15f;
            if (text.Length > 9)
            {
                size *= 9f / text.Length;
            }

            return Math.Max(8, (int)size);
        }

        private static void DrawGlyph(BitmapBuilder b, Glyph g, float cx, float cy, float r, BitmapColor fg, BitmapColor bg)
        {
            var t = Math.Max(2f, r * 0.13f);
            switch (g)
            {
                case Glyph.Scissors:
                    // Two blades crossing at the pivot + two finger rings.
                    b.DrawLine(cx - r * 0.55f, cy + r * 0.35f, cx + r * 0.55f, cy - r * 0.95f, fg, t);
                    b.DrawLine(cx + r * 0.55f, cy + r * 0.35f, cx - r * 0.55f, cy - r * 0.95f, fg, t);
                    Ring(b, cx - r * 0.62f, cy + r * 0.62f, r * 0.34f, fg, bg, t);
                    Ring(b, cx + r * 0.62f, cy + r * 0.62f, r * 0.34f, fg, bg, t);
                    b.FillCircle(cx, cy - r * 0.12f, t * 0.9f, fg);
                    break;

                case Glyph.NewCapture:
                    DashedRect(b, cx - r, cy - r * 0.75f, r * 2f, r * 1.5f, fg, t * 0.8f, r * 0.22f);
                    b.DrawLine(cx - r * 0.35f, cy, cx + r * 0.35f, cy, Palette.Green, t * 1.2f);
                    b.DrawLine(cx, cy - r * 0.35f, cx, cy + r * 0.35f, Palette.Green, t * 1.2f);
                    break;

                case Glyph.Rectangle:
                    DashedRect(b, cx - r, cy - r * 0.75f, r * 2f, r * 1.5f, fg, t * 0.8f, r * 0.22f);
                    break;

                case Glyph.Window:
                    Rect(b, cx - r * 0.55f, cy - r * 0.9f, r * 1.45f, r * 1.1f, Palette.Grey, t * 0.8f);
                    b.FillRectangle((int)(cx - r * 0.9f), (int)(cy - r * 0.45f), (int)(r * 1.45f), (int)(r * 1.2f), bg);
                    Rect(b, cx - r * 0.9f, cy - r * 0.45f, r * 1.45f, r * 1.2f, fg, t * 0.8f);
                    b.FillRectangle((int)(cx - r * 0.9f), (int)(cy - r * 0.45f), (int)(r * 1.45f), (int)(t * 2.2f), fg);
                    break;

                case Glyph.Freeform:
                    // Dotted closed blob.
                    for (var a = 0; a < 360; a += 24)
                    {
                        var rad = a * Math.PI / 180;
                        var rr = r * (0.82f + 0.18f * (float)Math.Sin(rad * 3));
                        b.FillCircle(cx + (float)Math.Cos(rad) * rr, cy + (float)Math.Sin(rad) * rr * 0.8f, t * 0.7f, fg);
                    }

                    break;

                case Glyph.Fullscreen:
                    // Four white corners.
                    var k = r * 0.45f;
                    Corner(b, cx - r, cy - r * 0.8f, k, 1, 1, fg, t);
                    Corner(b, cx + r, cy - r * 0.8f, k, -1, 1, fg, t);
                    Corner(b, cx - r, cy + r * 0.8f, k, 1, -1, fg, t);
                    Corner(b, cx + r, cy + r * 0.8f, k, -1, -1, fg, t);
                    break;

                case Glyph.Folder:
                    b.FillRectangle((int)(cx - r), (int)(cy - r * 0.72f), (int)(r * 0.85f), (int)(r * 0.3f), Palette.FolderYellow);
                    b.FillRectangle((int)(cx - r), (int)(cy - r * 0.5f), (int)(r * 2f), (int)(r * 1.3f), Palette.FolderYellow);
                    b.FillRectangle((int)(cx - r * 0.9f), (int)(cy - r * 0.25f), (int)(r * 1.8f), (int)(t * 0.9f), bg);
                    break;

                case Glyph.Delay:
                    // Stopwatch: ring + hands + top button.
                    Ring(b, cx, cy + r * 0.1f, r * 0.85f, fg, bg, t);
                    b.DrawLine(cx, cy + r * 0.1f, cx, cy - r * 0.45f, fg, t);
                    b.DrawLine(cx, cy + r * 0.1f, cx + r * 0.38f, cy + r * 0.1f, fg, t);
                    b.FillRectangle((int)(cx - r * 0.18f), (int)(cy - r * 1.0f), (int)(r * 0.36f), (int)(t * 1.4f), fg);
                    break;

                case Glyph.Cancel:
                    b.DrawLine(cx - r * 0.6f, cy - r * 0.6f, cx + r * 0.6f, cy + r * 0.6f, fg, t * 1.3f);
                    b.DrawLine(cx + r * 0.6f, cy - r * 0.6f, cx - r * 0.6f, cy + r * 0.6f, fg, t * 1.3f);
                    break;

                case Glyph.LastCapture:
                    // Picture frame with a mountain and a sun.
                    Rect(b, cx - r, cy - r * 0.75f, r * 2f, r * 1.5f, fg, t * 0.8f);
                    b.DrawLine(cx - r * 0.8f, cy + r * 0.55f, cx - r * 0.2f, cy - r * 0.15f, fg, t);
                    b.DrawLine(cx - r * 0.2f, cy - r * 0.15f, cx + r * 0.25f, cy + r * 0.35f, fg, t);
                    b.DrawLine(cx + r * 0.1f, cy + r * 0.2f, cx + r * 0.45f, cy - r * 0.1f, fg, t);
                    b.DrawLine(cx + r * 0.45f, cy - r * 0.1f, cx + r * 0.8f, cy + r * 0.55f, fg, t);
                    b.FillCircle(cx + r * 0.5f, cy - r * 0.42f, r * 0.14f, Palette.FolderYellow);
                    break;

                case Glyph.VideoCamera:
                    // Camera body + lens triangle.
                    b.FillRectangle((int)(cx - r), (int)(cy - r * 0.5f), (int)(r * 1.35f), (int)(r * 1.0f), fg);
                    b.DrawLine(cx + r * 0.4f, cy, cx + r, cy - r * 0.45f, fg, t * 1.2f);
                    b.DrawLine(cx + r * 0.4f, cy, cx + r, cy + r * 0.45f, fg, t * 1.2f);
                    b.DrawLine(cx + r, cy - r * 0.45f, cx + r, cy + r * 0.45f, fg, t * 1.2f);
                    break;

                case Glyph.VideoFolder:
                    DrawGlyph(b, Glyph.Folder, cx, cy, r, fg, bg);
                    b.FillRectangle((int)(cx - r * 0.35f), (int)(cy - r * 0.05f), (int)(r * 0.5f), (int)(r * 0.4f), Palette.Red);
                    b.DrawLine(cx + r * 0.15f, cy + r * 0.15f, cx + r * 0.45f, cy - r * 0.02f, Palette.Red, t);
                    b.DrawLine(cx + r * 0.15f, cy + r * 0.15f, cx + r * 0.45f, cy + r * 0.32f, Palette.Red, t);
                    break;

                case Glyph.LastVideo:
                    Rect(b, cx - r, cy - r * 0.75f, r * 2f, r * 1.5f, fg, t * 0.8f);
                    // Play triangle.
                    b.DrawLine(cx - r * 0.3f, cy - r * 0.4f, cx - r * 0.3f, cy + r * 0.4f, fg, t * 1.2f);
                    b.DrawLine(cx - r * 0.3f, cy - r * 0.4f, cx + r * 0.45f, cy, fg, t * 1.2f);
                    b.DrawLine(cx - r * 0.3f, cy + r * 0.4f, cx + r * 0.45f, cy, fg, t * 1.2f);
                    break;

                case Glyph.RecordDot:
                    b.FillCircle(cx, cy, r * 0.7f, Palette.Red);
                    break;

                case Glyph.StopSquare:
                    b.FillRectangle((int)(cx - r * 0.55f), (int)(cy - r * 0.55f), (int)(r * 1.1f), (int)(r * 1.1f), fg);
                    break;

                case Glyph.PauseBars:
                    b.FillRectangle((int)(cx - r * 0.55f), (int)(cy - r * 0.7f), (int)(r * 0.38f), (int)(r * 1.4f), fg);
                    b.FillRectangle((int)(cx + r * 0.17f), (int)(cy - r * 0.7f), (int)(r * 0.38f), (int)(r * 1.4f), fg);
                    break;

                case Glyph.Microphone:
                    b.FillRectangle((int)(cx - r * 0.25f), (int)(cy - r * 0.85f), (int)(r * 0.5f), (int)(r * 1.0f), fg);
                    b.FillCircle(cx, cy - r * 0.85f, r * 0.25f, fg);
                    b.FillCircle(cx, cy + r * 0.15f, r * 0.25f, fg);
                    b.DrawLine(cx - r * 0.55f, cy, cx - r * 0.55f, cy + r * 0.2f, fg, t);
                    b.DrawLine(cx + r * 0.55f, cy, cx + r * 0.55f, cy + r * 0.2f, fg, t);
                    b.DrawLine(cx - r * 0.55f, cy + r * 0.2f, cx, cy + r * 0.6f, fg, t);
                    b.DrawLine(cx + r * 0.55f, cy + r * 0.2f, cx, cy + r * 0.6f, fg, t);
                    b.DrawLine(cx, cy + r * 0.6f, cx, cy + r * 0.9f, fg, t);
                    break;

                case Glyph.Trash:
                    b.FillRectangle((int)(cx - r * 0.7f), (int)(cy - r * 0.7f), (int)(r * 1.4f), (int)(t * 1.2f), fg);
                    b.FillRectangle((int)(cx - r * 0.2f), (int)(cy - r * 0.9f), (int)(r * 0.4f), (int)(t * 1.2f), fg);
                    Rect(b, cx - r * 0.55f, cy - r * 0.5f, r * 1.1f, r * 1.35f, fg, t);
                    break;

                case Glyph.Pen:
                {
                    var tip = Accent ?? Palette.Red;
                    b.DrawLine(cx - r * 0.7f, cy + r * 0.7f, cx + r * 0.6f, cy - r * 0.6f, fg, t * 2.2f);
                    b.DrawLine(cx - r * 0.7f, cy + r * 0.7f, cx - r * 0.45f, cy + r * 0.45f, tip, t * 2.2f);
                    b.FillCircle(cx - r * 0.82f, cy + r * 0.82f, t * 0.9f, tip);
                    break;
                }

                case Glyph.Highlighter:
                {
                    var tip = Accent ?? Palette.FolderYellow;
                    b.DrawLine(cx - r * 0.5f, cy + r * 0.5f, cx + r * 0.6f, cy - r * 0.6f, fg, t * 3.2f);
                    b.DrawLine(cx - r * 0.75f, cy + r * 0.75f, cx - r * 0.45f, cy + r * 0.45f, tip, t * 3.2f);
                    b.FillRectangle((int)(cx - r), (int)(cy + r * 0.8f), (int)(r * 0.9f), (int)(t * 1.4f), tip);
                    break;
                }

                case Glyph.Eraser:
                    b.DrawLine(cx - r * 0.55f, cy + r * 0.35f, cx + r * 0.35f, cy - r * 0.55f, fg, t * 4.2f);
                    b.DrawLine(cx - r * 0.75f, cy + r * 0.55f, cx - r * 0.35f, cy + r * 0.15f, Palette.Grey, t * 4.2f);
                    b.DrawLine(cx - r * 0.9f, cy + r * 0.9f, cx + r * 0.9f, cy + r * 0.9f, fg, t * 0.8f);
                    break;

                case Glyph.Undo:
                    Arrow(b, cx + r * 0.7f, cy + r * 0.1f, cx - r * 0.7f, cy + r * 0.1f, fg, t * 1.2f, r * 0.4f);
                    b.DrawLine(cx + r * 0.7f, cy + r * 0.1f, cx + r * 0.7f, cy + r * 0.6f, fg, t * 1.2f);
                    break;

                case Glyph.Redo:
                    Arrow(b, cx - r * 0.7f, cy + r * 0.1f, cx + r * 0.7f, cy + r * 0.1f, fg, t * 1.2f, r * 0.4f);
                    b.DrawLine(cx - r * 0.7f, cy + r * 0.1f, cx - r * 0.7f, cy + r * 0.6f, fg, t * 1.2f);
                    break;

                case Glyph.Copy:
                    Rect(b, cx - r * 0.75f, cy - r * 0.55f, r * 1.1f, r * 1.3f, Palette.Grey, t);
                    b.FillRectangle((int)(cx - r * 0.35f), (int)(cy - r * 0.85f), (int)(r * 1.1f), (int)(r * 1.3f), bg);
                    Rect(b, cx - r * 0.35f, cy - r * 0.85f, r * 1.1f, r * 1.3f, fg, t);
                    break;

                case Glyph.Save:
                    Rect(b, cx - r * 0.8f, cy - r * 0.8f, r * 1.6f, r * 1.6f, fg, t);
                    b.FillRectangle((int)(cx - r * 0.45f), (int)(cy - r * 0.8f), (int)(r * 0.9f), (int)(r * 0.55f), fg);
                    b.FillRectangle((int)(cx - r * 0.5f), (int)(cy + r * 0.15f), (int)(r * 1.0f), (int)(r * 0.5f), fg);
                    break;

                case Glyph.Share:
                    b.FillCircle(cx - r * 0.6f, cy, r * 0.22f, fg);
                    b.FillCircle(cx + r * 0.55f, cy - r * 0.6f, r * 0.22f, fg);
                    b.FillCircle(cx + r * 0.55f, cy + r * 0.6f, r * 0.22f, fg);
                    b.DrawLine(cx - r * 0.6f, cy, cx + r * 0.55f, cy - r * 0.6f, fg, t);
                    b.DrawLine(cx - r * 0.6f, cy, cx + r * 0.55f, cy + r * 0.6f, fg, t);
                    break;

                case Glyph.Ruler:
                    b.DrawLine(cx - r * 0.85f, cy + r * 0.55f, cx + r * 0.85f, cy - r * 0.55f, fg, t * 4.5f);
                    for (var tick = -3; tick <= 3; tick++)
                    {
                        var px = cx + tick * r * 0.22f;
                        var py = cy - tick * r * 0.142f;
                        b.DrawLine(px, py, px + r * 0.12f, py + r * 0.19f, bg, t * 0.7f);
                    }

                    break;

                case Glyph.ZoomIn:
                    Magnifier(b, cx, cy, r, fg, bg, t);
                    b.DrawLine(cx - r * 0.5f, cy - r * 0.2f, cx + r * 0.1f, cy - r * 0.2f, fg, t);
                    b.DrawLine(cx - r * 0.2f, cy - r * 0.5f, cx - r * 0.2f, cy + r * 0.1f, fg, t);
                    break;

                case Glyph.ZoomOut:
                    Magnifier(b, cx, cy, r, fg, bg, t);
                    b.DrawLine(cx - r * 0.5f, cy - r * 0.2f, cx + r * 0.1f, cy - r * 0.2f, fg, t);
                    break;

                case Glyph.Zoom100:
                    Magnifier(b, cx, cy, r, fg, bg, t);
                    b.DrawLine(cx - r * 0.35f, cy - r * 0.45f, cx - r * 0.35f, cy + r * 0.05f, fg, t * 0.8f);
                    b.FillCircle(cx - r * 0.05f, cy - r * 0.2f, r * 0.12f, fg);
                    break;

                case Glyph.ZoomFit:
                    DrawGlyph(b, Glyph.Fullscreen, cx, cy, r * 0.95f, fg, bg);
                    b.FillRectangle((int)(cx - r * 0.35f), (int)(cy - r * 0.25f), (int)(r * 0.7f), (int)(r * 0.5f), fg);
                    break;

                case Glyph.Swatch:
                    b.FillCircle(cx, cy, r * 0.8f, fg);
                    b.FillCircle(cx, cy, r * 0.7f, Accent ?? Palette.Red);
                    break;

                case Glyph.Width:
                {
                    var w = Math.Clamp(WidthValue / 64f, 0.04f, 1f) * r * 1.4f;
                    b.DrawLine(cx - r * 0.85f, cy, cx + r * 0.85f, cy, Accent ?? fg, Math.Max(2f, w));
                    break;
                }

                case Glyph.Play:
                    b.DrawLine(cx - r * 0.35f, cy - r * 0.6f, cx - r * 0.35f, cy + r * 0.6f, fg, t * 1.4f);
                    b.DrawLine(cx - r * 0.35f, cy - r * 0.6f, cx + r * 0.6f, cy, fg, t * 1.4f);
                    b.DrawLine(cx - r * 0.35f, cy + r * 0.6f, cx + r * 0.6f, cy, fg, t * 1.4f);
                    break;

                case Glyph.Error:
                    b.FillCircle(cx, cy, r * 0.8f, Palette.Orange);
                    b.DrawText("!", (int)(cx - r), (int)(cy - r), (int)(r * 2), (int)(r * 2), Palette.Background, (int)(r * 1.2f));
                    break;

                case Glyph.Success:
                    b.DrawLine(cx - r * 0.6f, cy, cx - r * 0.15f, cy + r * 0.45f, Palette.Green, t * 1.6f);
                    b.DrawLine(cx - r * 0.15f, cy + r * 0.45f, cx + r * 0.7f, cy - r * 0.5f, Palette.Green, t * 1.6f);
                    break;

                // MicrophoneOff, Text, Redact, Settings, Diagnostic: SVG only (no drawn fallback needed by any key yet).
                case Glyph.Empty:
                default:
                    break;
            }
        }

        private static void Ring(BitmapBuilder b, float cx, float cy, float r, BitmapColor c, BitmapColor bg, float t)
        {
            b.FillCircle(cx, cy, r, c);
            b.FillCircle(cx, cy, Math.Max(1f, r - t), bg);
        }

        private static void Rect(BitmapBuilder b, float x, float y, float w, float h, BitmapColor c, float t)
        {
            b.DrawLine(x, y, x + w, y, c, t);
            b.DrawLine(x + w, y, x + w, y + h, c, t);
            b.DrawLine(x + w, y + h, x, y + h, c, t);
            b.DrawLine(x, y + h, x, y, c, t);
        }

        private static void DashedRect(BitmapBuilder b, float x, float y, float w, float h, BitmapColor c, float t, float dash)
        {
            DashedLine(b, x, y, x + w, y, c, t, dash);
            DashedLine(b, x + w, y, x + w, y + h, c, t, dash);
            DashedLine(b, x + w, y + h, x, y + h, c, t, dash);
            DashedLine(b, x, y + h, x, y, c, t, dash);
        }

        private static void DashedLine(BitmapBuilder b, float x1, float y1, float x2, float y2, BitmapColor c, float t, float dash)
        {
            var len = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            var n = Math.Max(1, (int)(len / dash));
            for (var i = 0; i < n; i += 2)
            {
                var a = (float)i / n;
                var e = Math.Min(1f, (float)(i + 1) / n);
                b.DrawLine(x1 + (x2 - x1) * a, y1 + (y2 - y1) * a, x1 + (x2 - x1) * e, y1 + (y2 - y1) * e, c, t);
            }
        }

        private static void Corner(BitmapBuilder b, float x, float y, float k, int dx, int dy, BitmapColor c, float t)
        {
            b.DrawLine(x, y, x + dx * k, y, c, t);
            b.DrawLine(x, y, x, y + dy * k, c, t);
        }
    }
}
