using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.Maui.Graphics;

namespace TorrentFree.Controls;

public sealed class LineChartView : GraphicsView
{
    public static readonly BindableProperty ValuesProperty = BindableProperty.Create(
        nameof(Values),
        typeof(IList<double>),
        typeof(LineChartView),
        default(IList<double>),
        propertyChanged: OnValuesChanged);

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor),
        typeof(Color),
        typeof(LineChartView),
        Colors.DodgerBlue,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty FillColorProperty = BindableProperty.Create(
        nameof(FillColor),
        typeof(Color),
        typeof(LineChartView),
        Colors.Transparent,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty StrokeThicknessProperty = BindableProperty.Create(
        nameof(StrokeThickness),
        typeof(float),
        typeof(LineChartView),
        2f,
        propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty MaxValueProperty = BindableProperty.Create(
        nameof(MaxValue),
        typeof(double),
        typeof(LineChartView),
        0d,
        propertyChanged: OnAppearanceChanged);

    private readonly LineChartDrawable _drawable;
    private INotifyCollectionChanged? _currentCollection;

    public LineChartView()
    {
        HeightRequest = 48;
        _drawable = new LineChartDrawable(this);
        Drawable = _drawable;
    }

    public IList<double>? Values
    {
        get => (IList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public float StrokeThickness
    {
        get => (float)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    private static void OnValuesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is LineChartView view)
        {
            view.Unsubscribe(oldValue as INotifyCollectionChanged);
            view.Subscribe(newValue as INotifyCollectionChanged);
            view.Invalidate();
        }
    }

    private static void OnAppearanceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is LineChartView view)
        {
            view.Invalidate();
        }
    }

    private void Subscribe(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        _currentCollection = collection;
        _currentCollection.CollectionChanged += OnCollectionChanged;
    }

    private void Unsubscribe(INotifyCollectionChanged? collection)
    {
        if (collection is null)
        {
            return;
        }

        collection.CollectionChanged -= OnCollectionChanged;
        if (ReferenceEquals(_currentCollection, collection))
        {
            _currentCollection = null;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Invalidate();
    }

    private sealed class LineChartDrawable(LineChartView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var values = owner.Values;
            if (values is null || values.Count == 0)
            {
                return;
            }

            var maxValue = owner.MaxValue > 0 ? owner.MaxValue : values.Max();
            if (maxValue <= 0)
            {
                maxValue = 1;
            }

            var count = values.Count;
            var width = dirtyRect.Width;
            var height = dirtyRect.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var stepX = count > 1 ? width / (count - 1) : width;

            var points = new List<PointF>(count);
            var path = new PathF();
            for (var i = 0; i < count; i++)
            {
                var value = Math.Clamp(values[i], 0, maxValue);
                var x = (float)(dirtyRect.Left + i * stepX);
                var y = (float)(dirtyRect.Bottom - (value / maxValue) * height);
                points.Add(new PointF(x, y));

                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            if (owner.FillColor != Colors.Transparent && points.Count > 0)
            {
                var fillPath = new PathF();
                fillPath.MoveTo(points[0]);
                for (var i = 1; i < points.Count; i++)
                {
                    fillPath.LineTo(points[i]);
                }
                fillPath.LineTo(dirtyRect.Right, dirtyRect.Bottom);
                fillPath.LineTo(dirtyRect.Left, dirtyRect.Bottom);
                fillPath.Close();
                canvas.FillColor = owner.FillColor;
                canvas.FillPath(fillPath);
            }

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = owner.StrokeThickness;
            canvas.DrawPath(path);
        }
    }
}
