using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioMeshParameterModel : INotifyPropertyChanged
{
    private string _valueText;

    public RekallAgeStudioMeshParameterModel(RekallAgeMeshOperationParameterDescriptor descriptor)
    {
        Descriptor = descriptor;
        _valueText = descriptor.DefaultValue.HasValue
            ? descriptor.ValueType == RekallAgeGeometryValueType.String
                ? descriptor.DefaultValue.Value.GetString() ?? string.Empty
                : descriptor.DefaultValue.Value.GetRawText()
            : string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public RekallAgeMeshOperationParameterDescriptor Descriptor { get; }
    public string Name => Descriptor.Name;
    public string TypeLabel => Descriptor.ValueType.ToString();
    public string RequirementLabel => Descriptor.Required ? "required" : "optional";
    public string Description => Descriptor.Description;
    public bool IsValid => !Descriptor.Required && string.IsNullOrWhiteSpace(ValueText) || TryGetValue(out _);

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (string.Equals(_valueText, value, StringComparison.Ordinal)) return;
            _valueText = value; PropertyChanged?.Invoke(this, new(nameof(ValueText))); PropertyChanged?.Invoke(this, new(nameof(IsValid)));
        }
    }

    public bool TryGetValue(out JsonNode? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(ValueText)) return !Descriptor.Required;
        switch (Descriptor.ValueType)
        {
            case RekallAgeGeometryValueType.Float when double.TryParse(ValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number):
                value = JsonValue.Create(number); return true;
            case RekallAgeGeometryValueType.Int32 when int.TryParse(ValueText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer):
                value = JsonValue.Create(integer); return true;
            case RekallAgeGeometryValueType.Bool when bool.TryParse(ValueText, out var boolean):
                value = JsonValue.Create(boolean); return true;
            case RekallAgeGeometryValueType.String when ValueText.Length <= 128:
                value = JsonValue.Create(ValueText); return true;
            default:
                return false;
        }
    }
}
