using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioInspectorEditorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? BooleanTemplate { get; set; }
    public DataTemplate? NumberTemplate { get; set; }
    public DataTemplate? IntegerTemplate { get; set; }
    public DataTemplate? EnumTemplate { get; set; }
    public DataTemplate? AssetRefTemplate { get; set; }
    public DataTemplate? EntityRefTemplate { get; set; }
    public DataTemplate? ColorTemplate { get; set; }
    public DataTemplate? Vector2Template { get; set; }
    public DataTemplate? Vector3Template { get; set; }
    public DataTemplate? Vector4Template { get; set; }
    public DataTemplate? JsonTemplate { get; set; }
    public DataTemplate? StringTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not RekallAgeStudioInspectorPropertyEditorModel row)
        {
            return base.SelectTemplate(item, container);
        }

        return row.TemplateKind switch
        {
            "boolean" => BooleanTemplate,
            "number" => NumberTemplate,
            "integer" => IntegerTemplate,
            "enum" => EnumTemplate,
            "assetRef" => AssetRefTemplate,
            "entityRef" => EntityRefTemplate,
            "color" => ColorTemplate,
            "vector2" => Vector2Template,
            "vector3" => Vector3Template,
            "vector4" => Vector4Template,
            "json" => JsonTemplate,
            _ => StringTemplate
        } ?? StringTemplate;
    }
}

public sealed class RekallAgeStudioColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || !TryParse(text, out var color)) return Brushes.Transparent;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static bool TryParse(string text, out Color color)
    {
        color = default;
        if ((text.Length != 7 && text.Length != 9)
            || text[0] != '#'
            || text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") >= 0)
        {
            return false;
        }

        var red = byte.Parse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = byte.Parse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = byte.Parse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var alpha = text.Length == 9
            ? byte.Parse(text.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.MaxValue;
        color = Color.FromArgb(alpha, red, green, blue);
        return true;
    }
}
