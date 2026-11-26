using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PickMoney.App.ViewModels;

namespace PickMoney.App.Controls;

/// <summary>
/// 净值曲线控件。纯 OnRender 绘制，无需第三方图表库。
/// </summary>
public class NetValueChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(ObservableCollection<NetValuePoint>),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinValueProperty = DependencyProperty.Register(
        nameof(MinValue),
        typeof(decimal),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(0.9m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue),
        typeof(decimal),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(1.1m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineValueProperty = DependencyProperty.Register(
        nameof(BaselineValue),
        typeof(decimal),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(1.0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush),
        typeof(Brush),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
        nameof(BaselineBrush),
        typeof(Brush),
        typeof(NetValueChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>曲线数据点</summary>
    public ObservableCollection<NetValuePoint>? Points
    {
        get => (ObservableCollection<NetValuePoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>纵轴下界</summary>
    public decimal MinValue
    {
        get => (decimal)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    /// <summary>纵轴上界</summary>
    public decimal MaxValue
    {
        get => (decimal)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>基准线（初始净值 1.000）</summary>
    public decimal BaselineValue
    {
        get => (decimal)GetValue(BaselineValueProperty);
        set => SetValue(BaselineValueProperty, value);
    }

    /// <summary>曲线颜色</summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>基准线颜色</summary>
    public Brush BaselineBrush
    {
        get => (Brush)GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    private const double LeftPadding = 52;
    private const double RightPadding = 16;
    private const double TopPadding = 16;
    private const double BottomPadding = 28;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var points = Points;
        var plotWidth = width - LeftPadding - RightPadding;
        var plotHeight = height - TopPadding - BottomPadding;
        if (plotWidth <= 0 || plotHeight <= 0)
        {
            return;
        }

        var min = MinValue;
        var max = MaxValue;
        if (max <= min)
        {
            max = min + 0.01m;
        }

        // 网格与 Y 轴刻度
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        var labelTypeface = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var labelFormat = new FormattedText(
            string.Empty,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            labelTypeface,
            11,
            labelBrush,
            96);

        const int gridLines = 5;
        for (var i = 0; i <= gridLines; i++)
        {
            var ratio = (double)i / gridLines;
            var y = TopPadding + plotHeight * ratio;

            drawingContext.DrawLine(gridPen, new Point(LeftPadding, y), new Point(LeftPadding + plotWidth, y));

            var value = max - (max - min) * (decimal)ratio;
            var text = value.ToString("F3", CultureInfo.InvariantCulture);
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                labelTypeface,
                11,
                labelBrush,
                96);
            formatted.MaxTextWidth = LeftPadding - 6;
            formatted.MaxTextHeight = 16;
            drawingContext.DrawText(formatted, new Point(LeftPadding - 6 - formatted.Width, y - formatted.Height / 2));
        }

        if (points is null || points.Count == 0)
        {
            DrawEmptyHint(drawingContext, labelTypeface, labelBrush, width, height);
            return;
        }

        // 基准线（净值 1.000）
        var baselineRatio = (double)((BaselineValue - min) / (max - min));
        var baselineY = TopPadding + plotHeight * (1 - baselineRatio);
        if (baselineY >= TopPadding && baselineY <= TopPadding + plotHeight)
        {
            var baselinePen = new Pen(BaselineBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            drawingContext.DrawLine(baselinePen, new Point(LeftPadding, baselineY), new Point(LeftPadding + plotWidth, baselineY));
        }

        // 计算点坐标
        var screenPoints = new Point[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var x = points.Count == 1
                ? LeftPadding + plotWidth / 2
                : LeftPadding + plotWidth * i / (points.Count - 1);

            var ratio = (double)((points[i].NetValue - min) / (max - min));
            var y = TopPadding + plotHeight * (1 - ratio);
            screenPoints[i] = new Point(x, y);
        }

        // 曲线下方填充
        if (screenPoints.Length >= 2)
        {
            var fillGeometry = new StreamGeometry();
            using (var ctx = fillGeometry.Open())
            {
                ctx.BeginFigure(screenPoints[0], isFilled: true, isClosed: true);
                ctx.LineTo(screenPoints[0], isStroked: true, isSmoothJoin: false);
                for (var i = 1; i < screenPoints.Length; i++)
                {
                    ctx.LineTo(screenPoints[i], isStroked: true, isSmoothJoin: false);
                }

                ctx.LineTo(new Point(screenPoints[^1].X, TopPadding + plotHeight), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(new Point(screenPoints[0].X, TopPadding + plotHeight), isStroked: true, isSmoothJoin: false);
            }

            fillGeometry.Freeze();
            var fillBrush = new LinearGradientBrush(
                Color.FromArgb(0x50, 0x25, 0x63, 0xEB),
                Color.FromArgb(0x05, 0x25, 0x63, 0xEB),
                new Point(0, 0),
                new Point(0, 1));
            drawingContext.DrawGeometry(fillBrush, null, fillGeometry);
        }

        // 折线
        if (screenPoints.Length >= 2)
        {
            var stream = new StreamGeometry();
            using (var ctx = stream.Open())
            {
                ctx.BeginFigure(screenPoints[0], isFilled: false, isClosed: false);
                for (var i = 1; i < screenPoints.Length; i++)
                {
                    ctx.LineTo(screenPoints[i], isStroked: true, isSmoothJoin: false);
                }
            }

            stream.Freeze();
            drawingContext.DrawGeometry(null, new Pen(LineBrush, 2), stream);
        }

        // 数据点
        foreach (var point in screenPoints)
        {
            drawingContext.DrawEllipse(Brushes.White, new Pen(LineBrush, 1.8), point, 3.2, 3.2);
        }

        // X 轴日期标签（首、中、尾）
        if (points.Count > 0)
        {
            var indexes = points.Count == 1
                ? new[] { 0 }
                : new[] { 0, points.Count / 2, points.Count - 1 };

            foreach (var index in indexes.Distinct())
            {
                var text = points[index].Date.ToString("MM-dd", CultureInfo.InvariantCulture);
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    labelTypeface,
                    11,
                    labelBrush,
                    96);

                var x = screenPoints[index].X - formatted.Width / 2;
                x = Math.Max(LeftPadding, Math.Min(x, LeftPadding + plotWidth - formatted.Width));
                drawingContext.DrawText(formatted, new Point(x, TopPadding + plotHeight + 6));
            }
        }
    }

    private static void DrawEmptyHint(DrawingContext drawingContext, Typeface typeface, Brush brush, double width, double height)
    {
        var hint = new FormattedText(
            "暂无净值历史数据",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            13,
            brush,
            96);
        drawingContext.DrawText(hint, new Point((width - hint.Width) / 2, (height - hint.Height) / 2));
    }
}
