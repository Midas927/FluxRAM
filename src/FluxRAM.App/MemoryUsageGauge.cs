using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Size = System.Windows.Size;

namespace FluxRAM.App;

public sealed class MemoryUsageGauge : ProgressBar
{
    static MemoryUsageGauge()
    {
        ValueProperty.OverrideMetadata(typeof(MemoryUsageGauge),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
        ForegroundProperty.OverrideMetadata(typeof(MemoryUsageGauge),
            new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public MemoryUsageGauge()
    {
        // Keep native range automation; replace only the linear visual.
        Template = null;
        Focusable = false;
    }

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(MemoryUsageGauge),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 7;
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        DrawArc(context, center, radius, 300, TrackBrush);
        var sweep = Math.Clamp(Value, 0, 100) * 3;
        if (sweep > 0) DrawArc(context, center, radius, sweep, Foreground);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double sweep, Brush brush)
    {
        Point At(double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180),
            center.Y + radius * Math.Sin(angle * Math.PI / 180));
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(120), false, false);
            path.ArcTo(At(120 + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(brush, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }
}
