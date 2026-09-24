using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Views;

/// <summary>Visible when the value is non-null (or null with ConverterParameter=invert).</summary>
public sealed class NullToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StateToBrush : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is RowState s ? s switch
        {
            RowState.Current => "Accent",
            RowState.Update => "Warn",
            RowState.Missing => "Danger",
            _ => "Dim",
        } : "Dim";
        return (Brush)Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Checks a RadioButton when the bound string equals ConverterParameter.</summary>
public sealed class EqualsToBool : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value as string, parameter as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter! : Binding.DoNothing;
}
