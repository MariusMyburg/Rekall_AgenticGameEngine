using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

/// <summary>
/// Same shape and role as <see cref="RekallAgeStudioModelingGraphParameterModel"/>, for material
/// graph node parameters instead of procedural geometry node parameters.
/// </summary>
public sealed class RekallAgeStudioMaterialGraphParameterModel : INotifyPropertyChanged
{
    private readonly RekallAgeMaterialParameterDescriptor _descriptor;
    private JsonNode? _acceptedValue;
    private string _valueText;

    public RekallAgeStudioMaterialGraphParameterModel(
        RekallAgeMaterialParameterDescriptor descriptor,
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
            case RekallAgeMaterialValueType.Float:
                if (!double.TryParse(ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number) || !WithinBounds(number)) return false;
                value = JsonValue.Create(number);
                return true;
            case RekallAgeMaterialValueType.Vector2:
                return TryParseVector(2, out value);
            case RekallAgeMaterialValueType.Vector3:
            case RekallAgeMaterialValueType.Normal:
                return TryParseVector(3, out value);
            // The catalog authors Color defaults as a "#rrggbb"/"#rrggbbaa" hex string (see
            // RekallAgeMaterialNodeCatalog.Color), not a numeric array, so it round-trips through
            // this editor the same way String does rather than through TryParseVector.
            case RekallAgeMaterialValueType.Color:
            case RekallAgeMaterialValueType.Texture2D:
            case RekallAgeMaterialValueType.Surface:
            case RekallAgeMaterialValueType.String:
                if (_descriptor.EnumChoices is { Count: > 0 } && !_descriptor.EnumChoices.Contains(ValueText, StringComparer.Ordinal)) return false;
                value = JsonValue.Create(ValueText);
                return true;
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

    private static string Format(JsonNode? value, RekallAgeMaterialValueType valueType)
    {
        if (value is null) return string.Empty;
        if (value is not JsonValue jsonValue) return value.ToJsonString();
        if (valueType is RekallAgeMaterialValueType.String or RekallAgeMaterialValueType.Texture2D or RekallAgeMaterialValueType.Surface or RekallAgeMaterialValueType.Color
            && jsonValue.TryGetValue<string>(out var text)) return text;
        if (jsonValue.TryGetValue<double>(out var number)) return number.ToString("R", CultureInfo.InvariantCulture);
        return value.ToJsonString();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
