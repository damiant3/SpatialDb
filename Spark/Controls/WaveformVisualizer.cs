using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark.Controls;

/// <summary>
/// Custom WPF control that renders an animated music visualizer.
/// Draws waveform, spectrum bars, and a playhead with colors that react
/// to the music's energy, tempo, and spectral characteristics.
/// </summary>
sealed class WaveformVisualizer : Control
{
    // ── Dependency properties ───────────────────────────────────

    public static readonly DependencyProperty AnalysisProperty =
        DependencyProperty.Register(nameof(Analysis), typeof(MusicAnalysis), typeof(WaveformVisualizer),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlaybackPositionProperty =
        DependencyProperty.Register(nameof(PlaybackPosition), typeof(double), typeof(WaveformVisualizer),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(WaveformVisualizer),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public MusicAnalysis? Analysis { get => (MusicAnalysis?)GetValue(AnalysisProperty); set => SetValue(AnalysisProperty, value); }
    public double PlaybackPosition { get => (double)GetValue(PlaybackPositionProperty); set => SetValue(PlaybackPositionProperty, value); }
    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

    // ── Render state ────────────────────────────────────────────

    readonly DispatcherTimer m_animTimer;
    float[] m_smoothBands = new float[64];
    double m_glowPhase;

    public WaveformVisualizer()
    {
        m_animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 fps
        m_animTimer.Tick += (_, _) => InvalidateVisual();
        ClipToBounds = true;
    }

    static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformVisualizer vis)
        {
            if ((bool)e.NewValue) vis.m_animTimer.Start();
            else vis.m_animTimer.Stop();
        }
    }

    // ── Rendering ───────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        MusicAnalysis? a = Analysis;

        // Background gradient — deep dark
        dc.DrawRectangle(
            new LinearGradientBrush(
                Color.FromRgb(0x08, 0x0E, 0x1A),
                Color.FromRgb(0x0D, 0x1B, 0x2A),
                90),
            null, new Rect(0, 0, w, h));

        if (a is null || a.Waveform.Length == 0)
        {
            // Idle state — draw placeholder text
            var ft = new FormattedText("♫ No track loaded",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 14,
                new SolidColorBrush(Color.FromArgb(0x60, 0xA2, 0xA2, 0xB0)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            return;
        }

        double pos = PlaybackPosition; // 0..1
        m_glowPhase += 0.03;

        // ── Spectrum bars (bottom half) ─────────────────────────
        DrawSpectrumBars(dc, a, w, h, pos);

        // ── Waveform (middle band) ──────────────────────────────
        DrawWaveform(dc, a, w, h, pos);

        // ── Playhead ────────────────────────────────────────────
        double px = pos * w;
        Pen playheadPen = new(new SolidColorBrush(Color.FromArgb(0xCC, 0xE9, 0x45, 0x60)), 2);
        dc.DrawLine(playheadPen, new Point(px, 0), new Point(px, h));

        // Glow dot on playhead
        double glowY = h / 2 + Math.Sin(m_glowPhase * 3) * h * 0.1;
        var glowBrush = new RadialGradientBrush(
            Color.FromArgb(0xAA, 0xE9, 0x45, 0x60),
            Color.FromArgb(0x00, 0xE9, 0x45, 0x60));
        dc.DrawEllipse(glowBrush, null, new Point(px, glowY), 8, 8);

        // ── Time labels ─────────────────────────────────────────
        DrawTimeLabels(dc, a, w, h, pos);
    }

    void DrawWaveform(DrawingContext dc, MusicAnalysis a, double w, double h, double pos)
    {
        float[] wf = a.Waveform;
        double midY = h * 0.45;
        double ampScale = h * 0.35;

        // Determine current energy for color shift
        int posIdx = (int)(pos * wf.Length);
        float energy = posIdx >= 0 && posIdx < wf.Length ? wf[Math.Clamp(posIdx, 0, wf.Length - 1)] : 0;

        // Color: shifts from cool blue → hot pink/gold based on energy
        byte r = (byte)(0x30 + energy * 0xB9);
        byte g = (byte)(0x60 + energy * 0x40);
        byte b = (byte)(0xDD - energy * 0x80);

        // Draw filled waveform
        StreamGeometry geo = new();
        using (StreamGeometryContext ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, midY), true, true);
            for (int i = 0; i < wf.Length; i++)
            {
                double x = i * w / wf.Length;
                double y = midY - wf[i] * ampScale;
                ctx.LineTo(new Point(x, y), true, false);
            }
            // Mirror bottom
            for (int i = wf.Length - 1; i >= 0; i--)
            {
                double x = i * w / wf.Length;
                double y = midY + wf[i] * ampScale * 0.6; // slightly smaller bottom
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();

        // Gradient fill — played portion is brighter
        double playX = pos * w;
        var wfBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xBB, r, g, b), 0),
                new GradientStop(Color.FromArgb(0xDD, r, g, b), Math.Max(0, pos - 0.01)),
                new GradientStop(Color.FromArgb(0x44, 0x30, 0x60, 0xDD), pos + 0.01),
                new GradientStop(Color.FromArgb(0x33, 0x20, 0x40, 0x99), 1),
            }
        };

        dc.DrawGeometry(wfBrush, null, geo);

        // Outline on the played portion
        Pen outlinePen = new(new SolidColorBrush(Color.FromArgb(0x66, r, g, b)), 1);
        dc.DrawGeometry(null, outlinePen, geo);
    }

    void DrawSpectrumBars(DrawingContext dc, MusicAnalysis a, double w, double h, double pos)
    {
        float[][] spec = a.SpectrogramBands;
        if (spec.Length == 0) return;

        int frameIdx = Math.Clamp((int)(pos * spec.Length), 0, spec.Length - 1);
        float[] bands = spec[frameIdx];
        if (bands.Length == 0) return;

        // Smooth the bands for buttery animation
        for (int i = 0; i < bands.Length && i < m_smoothBands.Length; i++)
        {
            m_smoothBands[i] = m_smoothBands[i] * 0.7f + bands[i] * 0.3f;
        }

        double barW = w / m_smoothBands.Length;
        double maxBarH = h * 0.35;
        double baseY = h;

        // Find max for normalization
        float maxVal = 1f;
        for (int i = 0; i < m_smoothBands.Length; i++)
            if (m_smoothBands[i] > maxVal) maxVal = m_smoothBands[i];

        for (int i = 0; i < m_smoothBands.Length; i++)
        {
            float norm = m_smoothBands[i] / maxVal;
            double barH = norm * maxBarH;

            // Color gradient per bar — bass=purple, mids=blue, highs=cyan/gold
            double t = i / (double)m_smoothBands.Length;
            byte rb = (byte)(0x53 + t * 0x60);
            byte gb = (byte)(0x34 + t * 0xCC);
            byte bb = (byte)(0x83 + t * 0x40);
            byte alpha = (byte)(0x44 + norm * 0x88);

            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(alpha, rb, gb, bb)),
                null,
                new Rect(i * barW + 1, baseY - barH, Math.Max(1, barW - 2), barH));

            // Glow cap on tall bars
            if (norm > 0.5)
            {
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb((byte)(norm * 0xAA), 0xFF, 0xD7, 0x00)),
                    null,
                    new Rect(i * barW + 1, baseY - barH - 2, Math.Max(1, barW - 2), 2));
            }
        }
    }

    void DrawTimeLabels(DrawingContext dc, MusicAnalysis a, double w, double h, double pos)
    {
        var typeface = new Typeface("Consolas");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Brush dimBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xA2, 0xA2, 0xB0));

        TimeSpan current = TimeSpan.FromSeconds(pos * a.DurationSeconds);
        TimeSpan total = TimeSpan.FromSeconds(a.DurationSeconds);
        string timeText = $"{current:m\\:ss} / {total:m\\:ss}";

        var ft = new FormattedText(timeText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, 10, dimBrush, dpi);
        dc.DrawText(ft, new Point(w - ft.Width - 6, 4));

        // BPM badge
        if (a.EstimatedBpm > 0)
        {
            string bpmText = $"♩ {a.EstimatedBpm:F0} BPM";
            var bpmFt = new FormattedText(bpmText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 9,
                new SolidColorBrush(Color.FromArgb(0x88, 0xE9, 0x45, 0x60)), dpi);
            dc.DrawText(bpmFt, new Point(6, 4));
        }
    }
}
