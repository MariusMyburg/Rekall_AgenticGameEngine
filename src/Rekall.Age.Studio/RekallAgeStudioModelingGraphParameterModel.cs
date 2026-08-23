using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioModelingGraphParameterModel : INotifyPropertyChanged
{
    private readonly RekallAgeModelingParameterDescriptor _descriptor;
    private JsonNode? _acceptedValue;
    private string _valueText;

    public RekallAgeStudioModelingGraphParameterModel(
        RekallAgeModelingParameterDescriptor descriptor,
        JsonNode? currentValue)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _acceptedValue = (currentValue ?? descriptor.DefaultValue)?.DeepClone();
        _valueText = Format(_acceptedValue, descriptor.ValueType);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ParameterId => _descriptor.ParameterId;
    public string DisplayName => _descriptor.DisplayName;
    public string TypeLabel => _descriptor.ValueType.ToString();
    public string? Unit => _descriptor.Unit;
    public string? Description => _descriptor.Description;
    public IReadOnlyList<string> EnumChoices => _descriptor.EnumChoices ?? [];

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText.Equals(value, StringComparison.Ordinal)) return;
            _valueText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsValid));
            OnPropertyChanged(nameof(IsModified));
        }
    }

    public bool IsValid => TryGetValue(out _);
    public bool IsModified => TryGetValue(out var value) && !JsonNode.DeepEquals(value, _acceptedValue);

    public bool TryGetValue(out JsonNode? value)
    {
        value = null;
        switch (_descriptor.ValueType)
        {
            case RekallAgeModelingValueType.Scalar:
                if (!double.TryParse(ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number)
                    || !WithinBounds(number)) return false;
                value = JsonValue.Create(number);
                return true;
            case RekallAgeModelingValueType.Integer:
                if (!int.TryParse(ValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    || !WithinBounds(integer)) return false;
                value = JsonValue.Create(integer);
                return true;
            case RekallAgeModelingValueType.Boolean:
                if (!bool.TryParse(ValueText, out var boolean)) return false;
                value = JsonValue.Create(boolean);
                return true;
            case RekallAgeModelingValueType.String:
            case RekallAgeModelingValueType.Material:
                if (_descriptor.EnumChoices is { Count: > 0 }
                    && !_descriptor.EnumChoices.Contains(ValueText, StringComparer.Ordinal)) return false;
                value = JsonValue.Create(ValueText);
                return true;
            case RekallAgeModelingValueType.Vector2:
                return TryParseVector(2, out value);
            case RekallAgeModelingValueType.Vector3:
                return TryParseVector(3, out value);
            case RekallAgeModelingValueType.Vector4:
                return TryParseVector(4, out value);
            default:
                return false;
        }
    }

    internal void AcceptChanges()
    {
        if (!TryGetValue(out var value)) throw new InvalidOperationException($"Parameter '{ParameterId}' is invalid.");
        _acceptedValue = value?.DeepClone();
        OnPropertyChanged(nameof(IsModified));
    }

    private bool TryParseVector(int expectedCount, out JsonNode? value)
    {
        value = null;
        try
        {
            if (JsonNode.Parse(ValueText) is not JsonArray array || array.Count != expectedCount) return false;
            var result = new JsonArray();
            foreach (var item in array)
            {
                if (item is not JsonValue itemValue || !itemValue.TryGetValue<double>(out var number) || !double.IsFinite(number)) return false;
                result.Add(number);
            }
            value = result;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool WithinBounds(double value) =>
        (_descriptor.Minimum is null || value >= _descriptor.Minimum.Value)
        && (_descriptor.Maximum is null || value <= _descriptor.Maximum.Value);

    private static string Format(JsonNode? value, RekallAgeModelingValueType valueType)
    {
        if (value is null) return string.Empty;
        if (value is not JsonValue jsonValue) return value.ToJsonString();
        if (valueType is RekallAgeModelingValueType.String or RekallAgeModelingValueType.Material
            && jsonValue.TryGetValue<string>(out var text)) return text;
        if (jsonValue.TryGetValue<double>(out var number)) return number.ToString("R", CultureInfo.InvariantCulture);
        if (jsonValue.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if (jsonValue.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        return value.ToJsonString();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
