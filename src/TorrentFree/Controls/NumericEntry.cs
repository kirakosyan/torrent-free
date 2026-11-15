using System.Globalization;
using TorrentFree.Services;

namespace TorrentFree.Controls;

/// <summary>Normalizes text before updating a numeric binding, including empty and overflow input.</summary>
public sealed class NumericEntry : Entry
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(nameof(Value), typeof(double),
        typeof(NumericEntry), 0d, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((NumericEntry)b).FormatValue());
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(nameof(Maximum), typeof(double), typeof(NumericEntry), 1_000_000d);
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(nameof(Minimum), typeof(double), typeof(NumericEntry), 0d);
    public static readonly BindableProperty AllowDecimalProperty = BindableProperty.Create(nameof(AllowDecimal), typeof(bool), typeof(NumericEntry), false);
    public static readonly BindableProperty ShowZeroAsEmptyProperty = BindableProperty.Create(nameof(ShowZeroAsEmpty), typeof(bool), typeof(NumericEntry), false,
        propertyChanged: (b, _, _) => ((NumericEntry)b).FormatValue());
    private bool _updating;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public bool AllowDecimal { get => (bool)GetValue(AllowDecimalProperty); set => SetValue(AllowDecimalProperty, value); }
    public bool ShowZeroAsEmpty { get => (bool)GetValue(ShowZeroAsEmptyProperty); set => SetValue(ShowZeroAsEmptyProperty, value); }

    public NumericEntry()
    {
        Keyboard = Keyboard.Numeric;
        TextChanged += (_, _) => NormalizeText();
        Unfocused += (_, _) => { NormalizeText(); FormatValue(); };
        FormatValue();
    }

    private void NormalizeText()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var result = NumericInput.Parse(Text, AllowDecimal, Maximum, CultureInfo.CurrentCulture);
            if (Text != result.Text) Text = result.Text;
            Value = Math.Max(Minimum, result.Value);
        }
        finally { _updating = false; }
    }

    private void FormatValue()
    {
        if (_updating) return;
        _updating = true;
        try { Text = ShowZeroAsEmpty && Value == 0 ? string.Empty : Value.ToString(CultureInfo.CurrentCulture); }
        finally { _updating = false; }
    }
}
