using Microsoft.Maui.Layouts;

namespace TorrentFree.Controls;

// Use the same visible rows for measuring and arranging compact-card actions.
public sealed class ActionWrapLayout : HorizontalStackLayout
{
    protected override ILayoutManager CreateLayoutManager() => new ActionLayoutManager(this);

    private sealed class ActionLayoutManager(ActionWrapLayout layout) : HorizontalStackLayoutManager(layout)
    {
        private sealed class Row
        {
            public List<IView> Children { get; } = [];
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private List<Row> GetRows(double width)
        {
            List<Row> rows = [];
            var row = new Row();
            foreach (var child in layout.Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                var spacing = row.Children.Count == 0 ? 0 : layout.Spacing;
                if (row.Children.Count > 0 && row.Width + spacing + child.DesiredSize.Width > width)
                {
                    rows.Add(row);
                    row = new Row();
                    spacing = 0;
                }
                row.Children.Add(child);
                row.Width += spacing + child.DesiredSize.Width;
                row.Height = Math.Max(row.Height, child.DesiredSize.Height);
            }
            if (row.Children.Count > 0) rows.Add(row);
            return rows;
        }

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var width = Math.Max(0, widthConstraint - layout.Padding.HorizontalThickness);
            foreach (var child in layout.Children)
                if (child.Visibility != Visibility.Collapsed)
                    child.Measure(width, double.PositiveInfinity);
            var rows = GetRows(width);
            var desiredWidth = rows.Count == 0 ? 0 : rows.Max(row => row.Width);
            var desiredHeight = rows.Sum(row => row.Height) + Math.Max(0, rows.Count - 1) * layout.Spacing;
            return new Size(
                ResolveConstraints(widthConstraint, Stack.Width, desiredWidth + layout.Padding.HorizontalThickness, Stack.MinimumWidth, Stack.MaximumWidth),
                ResolveConstraints(heightConstraint, Stack.Height, desiredHeight + layout.Padding.VerticalThickness, Stack.MinimumHeight, Stack.MaximumHeight));
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var width = Math.Max(0, bounds.Width - layout.Padding.HorizontalThickness);
            var top = bounds.Top + layout.Padding.Top;
            var rtl = layout.FlowDirection == FlowDirection.RightToLeft;
            foreach (var row in GetRows(width))
            {
                var x = bounds.Left + layout.Padding.Left + (rtl ? row.Width : width - row.Width);
                foreach (var child in row.Children)
                {
                    var size = child.DesiredSize;
                    if (rtl) x -= size.Width;
                    child.Arrange(new Rect(x, top + (row.Height - size.Height) / 2, size.Width, size.Height));
                    x += rtl ? -layout.Spacing : size.Width + layout.Spacing;
                }
                top += row.Height + layout.Spacing;
            }
            return bounds.Size;
        }
    }
}
