using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace FluxRAM.App;

public sealed class MemoryTrend : FrameworkElement
{
    private readonly Queue<(DateTimeOffset Time, double Load)> _samples = new();
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(MemoryTrend), new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(MemoryTrend), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public void AddSample(DateTimeOffset time, double load)
    {
        if (_samples.Count > 0 && time < _samples.Last().Time) _samples.Clear();
        _samples.Enqueue((time, Math.Clamp(load, 0, 100)));
        while (_samples.Count > 120 || _samples.Peek().Time < time.AddMinutes(-2)) _samples.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = Math.Max(0, ActualWidth - 6);
        var height = Math.Max(0, ActualHeight - 6);
        var gridPen = new Pen(GridBrush, 1);
        for (var i = 0; i <= 4; i++)
            drawingContext.DrawLine(gridPen, new Point(3, 3 + height * i / 4), new Point(3 + width, 3 + height * i / 4));
        if (_samples.Count == 0) return;
        var end = _samples.Last().Time;
        var geometry = new StreamGeometry();
        Point last = default;
        using (var context = geometry.Open())
        {
            var first = true;
            foreach (var sample in _samples)
            {
                last = new Point(3 + width * (1 - (end - sample.Time).TotalSeconds / 120), 3 + height * (1 - sample.Load / 100));
                if (first) context.BeginFigure(last, false, false);
                else context.LineTo(last, true, false);
                first = false;
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(LineBrush, 2), geometry);
        drawingContext.DrawEllipse(LineBrush, null, last, 3, 3);
    }
}
