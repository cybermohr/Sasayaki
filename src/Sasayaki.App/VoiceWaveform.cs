using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Sasayaki.App;

/// <summary>A lightweight, microphone-reactive ribbon drawn without bitmap assets.</summary>
public sealed class VoiceWaveform : FrameworkElement
{
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Pen[] strands;
    private readonly Pen glow;
    private double target, envelope, lastSample, lastFrame, phase;
    private bool recording;

    public VoiceWaveform()
    {
        Height = 116;
        ClipToBounds = true;
        IsHitTestVisible = false;
        var brush = new LinearGradientBrush();
        brush.GradientStops.Add(new(Color.FromRgb(155, 66, 255), 0));
        brush.GradientStops.Add(new(Color.FromRgb(101, 101, 255), .28));
        brush.GradientStops.Add(new(Color.FromRgb(95, 227, 255), .5));
        brush.GradientStops.Add(new(Color.FromRgb(96, 94, 255), .7));
        brush.GradientStops.Add(new(Color.FromRgb(196, 64, 255), 1));
        brush.Freeze();
        strands = Enumerable.Range(0, 28).Select(i =>
        {
            var pen = new Pen(brush, i % 7 == 0 ? 1.25 : .65);
            pen.Freeze(); return pen;
        }).ToArray();
        glow = new Pen(brush, 7); glow.Freeze();
        timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => timer.Stop();
        Loaded += (_, _) => UpdateAnimation();
        System.Windows.Automation.AutomationProperties.SetName(this, "Microphone recording waveform");
    }

    public void SetRecording(bool value)
    {
        recording = value;
        target = envelope = 0;
        UpdateAnimation();
        InvalidateVisual();
    }

    public void SetLevel(float value)
    {
        if (!recording) return;
        target = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        lastSample = clock.Elapsed.TotalSeconds;
        if (!SystemParameters.ClientAreaAnimation) { envelope = target; InvalidateVisual(); }
    }

    private void UpdateAnimation()
    {
        if (IsVisible && recording && SystemParameters.ClientAreaAnimation)
        { lastFrame = clock.Elapsed.TotalSeconds; timer.Start(); }
        else timer.Stop();
    }

    private void Advance()
    {
        var now = clock.Elapsed.TotalSeconds;
        var dt = Math.Min(.1, now - lastFrame); lastFrame = now;
        if (now - lastSample > .18) target = 0;
        envelope += (target - envelope) * (1 - Math.Exp(-dt * (target > envelope ? 22 : 7)));
        phase += dt * (1.8 + envelope * 2);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        if (width <= 0) return;
        var center = ActualHeight / 2;
        var strength = recording ? .12 + .88 * Math.Sqrt(envelope) : .025;
        for (var line = 0; line < strands.Length; line++)
        {
            var offset = (line - 13.5) / 13.5;
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                for (var sample = 0; sample <= 144; sample++)
                {
                    var x = sample / 144d;
                    var taper = Math.Pow(Math.Sin(Math.PI * x), 1.7);
                    var wave = Math.Sin(x * Math.PI * 7 - phase + offset * .9)
                        * Math.Cos(x * Math.PI * 3 + phase * .43 + offset * .6);
                    var ribbon = wave * (.72 + offset * .2)
                        + offset * .28 * Math.Sin(x * Math.PI * 5 + phase * .7);
                    var point = new Point(x * width, center + ribbon * taper * strength * (center - 7));
                    if (sample == 0) context.BeginFigure(point, false, false);
                    else context.LineTo(point, true, false);
                }
            }
            path.Freeze();
            if (line % 7 == 0)
            {
                dc.PushOpacity(.055); dc.DrawGeometry(null, glow, path); dc.Pop();
            }
            dc.PushOpacity(line % 7 == 0 ? .9 : .42);
            dc.DrawGeometry(null, strands[line], path); dc.Pop();
        }
    }
}
