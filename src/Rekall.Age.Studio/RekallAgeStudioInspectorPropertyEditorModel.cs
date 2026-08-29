using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public sealed record RekallAgeStudioInspectorPropertyChoice(string DisplayName, string Value);

public sealed class RekallAgeStudioInspectorPropertyEditorModel : INotifyPropertyChanged
{
    private string _persistedDisplayValue;
    private bool _persistedIsDefined;
    private string _originalDraftSignature = string.Empty;
    private JsonNode? _originalValue;
    private bool _originalValueIsValid;
    private string _textValue = string.Empty;
    private bool? _booleanValue;
    private string _colorValue = string.Empty;
    private string _vectorX = string.Empty;
    private string _vectorY = string.Empty;
    private string _vectorZ = string.Empty;
    private string _vectorW = string.Empty;
    private string? _validationMessage;

    public RekallAgeStudioInspectorPropertyEditorModel(
        string componentType,
        RekallAgeInspectorPropertyModel property,
        IReadOnlyList<string>? assetChoices = null,
        IReadOnlyList<RekallAgeStudioInspectorPropertyChoice>? entityChoices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(property);

        ComponentType = componentType;
        Name = property.Name;
        TypeName = property.TypeName;
        EditorKind = property.EditorKind;
        TemplateKind = SelectTemplateKind(property);
        AssetKind = property.AssetKind;
        Minimum = property.Minimum;
        Maximum = property.Maximum;
        Description = property.Description;

        ChoiceItems = BuildChoiceItems(property, TemplateKind, assetChoices, entityChoices);
        Choices = ChoiceItems.Select(choice => choice.Value).ToArray();
        _persistedDisplayValue = property.Value;
        _persistedIsDefined = property.IsDefined;
        InitializeDraft(property.Value, property.IsDefined);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ComponentType { get; }
    public string Name { get; }
    public string TypeName { get; }
    public string EditorKind { get; }
    public string TemplateKind { get; }
    public string? AssetKind { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }
    public string? Description { get; }
    public IReadOnlyList<string> Choices { get; }
    public IReadOnlyList<RekallAgeStudioInspectorPropertyChoice> ChoiceItems { get; }
    public bool IsDefined { get; private set; }
    public string OriginalDisplayValue { get; private set; } = string.Empty;

    public string TextValue
    {
        get => _textValue;
        set => SetDraft(ref _textValue, value ?? string.Empty);
    }

    public bool? BooleanValue
    {
        get => _booleanValue;
        set => SetDraft(ref _booleanValue, value);
    }

    public string ColorValue
    {
        get => _colorValue;
        set => SetDraft(ref _colorValue, value ?? string.Empty);
    }

    public string VectorX
    {
        get => _vectorX;
        set => SetDraft(ref _vectorX, value ?? string.Empty);
    }

    public string VectorY
    {
        get => _vectorY;
        set => SetDraft(ref _vectorY, value ?? string.Empty);
    }

    public string VectorZ
    {
        get => _vectorZ;
        set => SetDraft(ref _vectorZ, value ?? string.Empty);
    }

    public string VectorW
    {
        get => _vectorW;
        set => SetDraft(ref _vectorW, value ?? string.Empty);
    }

    public bool IsDirty
    {
        get
        {
            if (TryCreateValueCore(out var value, out _))
            {
                return !_originalValueIsValid || !JsonNode.DeepEquals(value, _originalValue);
            }

            return !string.Equals(DraftSignature(), _originalDraftSignature, StringComparison.Ordinal);
        }
    }

    public bool IsValid => TryCreateValueCore(out _, out _);

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (string.Equals(_validationMessage, value, StringComparison.Ordinal)) return;
            _validationMessage = value;
            OnPropertyChanged();
        }
    }

    public bool TryCreateValue(out JsonNode? value, out string? error)
    {
        var valid = TryCreateValueCore(out value, out error);
        if (valid && TemplateKind.Equals("color", StringComparison.Ordinal)
            && value is JsonValue colorValue && colorValue.TryGetValue<string>(out var normalizedColor)
            && !string.Equals(_colorValue, normalizedColor, StringComparison.Ordinal))
        {
            _colorValue = normalizedColor;
            OnPropertyChanged(nameof(ColorValue));
            OnPropertyChanged(nameof(IsDirty));
        }
        ValidationMessage = error;
        OnPropertyChanged(nameof(IsValid));
        return valid;
    }

    public void AcceptPersistedValue(RekallAgeInspectorPropertyModel property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!property.Name.Equals(Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("The persisted property does not match this editor row.", nameof(property));
        }

        _persistedDisplayValue = property.Value;
        _persistedIsDefined = property.IsDefined;
        InitializeDraft(property.Value, property.IsDefined);
    }

    public void RestoreOriginalDraft() => InitializeDraft(_persistedDisplayValue, _persistedIsDefined);

    internal void SetServerValidation(string? message) => ValidationMessage = message;

    private static string SelectTemplateKind(RekallAgeInspectorPropertyModel property)
    {
        var kind = property.EditorKind.Trim();
        if (kind.Equals("assetRef", StringComparison.OrdinalIgnoreCase)) return "assetRef";
        if (kind.Equals("entityRef", StringComparison.OrdinalIgnoreCase)) return "entityRef";
        if (kind.Equals("boolean", StringComparison.OrdinalIgnoreCase)) return "boolean";
        if (kind.Equals("number", StringComparison.OrdinalIgnoreCase)) return "number";
        if (kind.Equals("integer", StringComparison.OrdinalIgnoreCase)) return "integer";
        if (kind.Equals("color", StringComparison.OrdinalIgnoreCase)) return "color";
        if (kind.Equals("vector2", StringComparison.OrdinalIgnoreCase)) return "vector2";
        if (kind.Equals("vector3", StringComparison.OrdinalIgnoreCase)) return "vector3";
        if (kind.Equals("vector4", StringComparison.OrdinalIgnoreCase)) return "vector4";
        if (kind.Equals("enum", StringComparison.OrdinalIgnoreCase)) return "enum";

        if (IsArrayType(property.TypeName)
            || kind.Equals("object", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (kind.Equals("string", StringComparison.OrdinalIgnoreCase))
        {
            return property.AllowedValues.Count > 0 ? "enum" : "string";
        }

        return "json";
    }

    private static IReadOnlyList<RekallAgeStudioInspectorPropertyChoice> BuildChoiceItems(
        RekallAgeInspectorPropertyModel property,
        string templateKind,
        IReadOnlyList<string>? assetChoices,
        IReadOnlyList<RekallAgeStudioInspectorPropertyChoice>? entityChoices) => templateKind switch
        {
            "assetRef" => (assetChoices ?? []).Select(value => new RekallAgeStudioInspectorPropertyChoice(value, value)).ToArray(),
            "entityRef" => (entityChoices ?? []).ToArray(),
            "enum" => property.AllowedValues.Select(value => new RekallAgeStudioInspectorPropertyChoice(value, value)).ToArray(),
            _ => []
        };

    private void InitializeDraft(string displayValue, bool isDefined)
    {
        var draftValue = DecodeStringDisplayValue(displayValue, isDefined);
        OriginalDisplayValue = displayValue;
        IsDefined = isDefined;
        _textValue = InitialTextValue(draftValue, isDefined);
        _booleanValue = isDefined && bool.TryParse(displayValue, out var boolean) ? boolean : null;
        _colorValue = isDefined ? draftValue : string.Empty;
        _vectorX = string.Empty;
        _vectorY = string.Empty;
        _vectorZ = string.Empty;
        _vectorW = string.Empty;
        InitializeVectorDraft(displayValue, isDefined);
        ValidationMessage = null;
        _originalDraftSignature = DraftSignature();
        _originalValueIsValid = TryCreateValueCore(out var value, out _);
        _originalValue = _originalValueIsValid ? value?.DeepClone() : null;

        OnPropertyChanged(nameof(OriginalDisplayValue));
        OnPropertyChanged(nameof(IsDefined));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(ColorValue));
        OnPropertyChanged(nameof(VectorX));
        OnPropertyChanged(nameof(VectorY));
        OnPropertyChanged(nameof(VectorZ));
        OnPropertyChanged(nameof(VectorW));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsValid));
    }

    private string DecodeStringDisplayValue(string displayValue, bool isDefined)
    {
        if (!isDefined || TemplateKind is not ("string" or "enum" or "assetRef" or "entityRef" or "color"))
        {
            return displayValue;
        }

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{displayValue}\"") ?? string.Empty;
        }
        catch (JsonException)
        {
            return displayValue;
        }
    }

    private string InitialTextValue(string displayValue, bool isDefined)
    {
        if (isDefined) return displayValue;
        if (TemplateKind.Equals("json", StringComparison.Ordinal)) return IsArrayType(TypeName) ? "[]" : "{}";
        return string.Empty;
    }

    private void InitializeVectorDraft(string displayValue, bool isDefined)
    {
        if (!isDefined || !TemplateKind.StartsWith("vector", StringComparison.Ordinal)) return;
        try
        {
            if (JsonNode.Parse(displayValue) is not JsonArray array) return;
            var values = new string[Math.Min(array.Count, 4)];
            for (var index = 0; index < values.Length; index++)
            {
                if (!TryReadFiniteJsonNumber(array[index], out var number)) return;
                values[index] = number.ToString("R", CultureInfo.InvariantCulture);
            }
            if (values.Length > 0) _vectorX = values[0];
            if (values.Length > 1) _vectorY = values[1];
            if (values.Length > 2) _vectorZ = values[2];
            if (values.Length > 3) _vectorW = values[3];
        }
        catch (JsonException)
        {
        }
    }

    private bool TryCreateValueCore(out JsonNode? value, out string? error)
    {
        switch (TemplateKind)
        {
            case "number":
                if (!TryParseFiniteNumber(TextValue, Name, out var number, out error)
                    || !ValidateRange(number, Name, out error))
                {
                    value = null;
                    return false;
                }
                value = JsonValue.Create(number);
                return true;

            case "integer":
                if (!long.TryParse(TextValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    value = null;
                    error = $"{Name} must be an integer.";
                    return false;
                }
                if (!ValidateRange(integer, Name, out error))
                {
                    value = null;
                    return false;
                }
                value = JsonValue.Create(integer);
                return true;

            case "boolean":
                if (BooleanValue is not { } boolean)
                {
                    value = null;
                    error = $"{Name} must be true or false.";
                    return false;
                }
                value = JsonValue.Create(boolean);
                error = null;
                return true;

            case "color":
                if (!TryParseColor(ColorValue, out var color, out error))
                {
                    value = null;
                    return false;
                }
                value = JsonValue.Create(color);
                return true;

            case "vector2":
            case "vector3":
            case "vector4":
                return TryParseVector(out value, out error);

            case "json":
                try
                {
                    value = JsonNode.Parse(TextValue);
                    error = null;
                    return true;
                }
                catch (JsonException exception)
                {
                    value = null;
                    error = $"{Name} must contain valid JSON: {exception.Message}";
                    return false;
                }

            case "enum":
                if (Choices.Count > 0 && !Choices.Contains(TextValue, StringComparer.Ordinal))
                {
                    value = null;
                    error = $"{Name} must be one of: {string.Join(", ", Choices)}.";
                    return false;
                }
                goto default;

            default:
                value = JsonValue.Create(TextValue);
                error = null;
                return true;
        }
    }

    private bool TryParseVector(out JsonNode? value, out string? error)
    {
        var count = TemplateKind[^1] - '0';
        var text = new[] { VectorX, VectorY, VectorZ, VectorW };
        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            var label = $"{Name} component {index + 1} of {count}";
            if (!TryParseFiniteNumber(text[index], label, out values[index], out error)
                || !ValidateRange(values[index], label, out error))
            {
                value = null;
                return false;
            }
        }

        value = new JsonArray(values.Select(number => (JsonNode?)JsonValue.Create(number)).ToArray());
        error = null;
        return true;
    }

    private static bool TryParseFiniteNumber(string text, string label, out double value, out string? error)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || !double.IsFinite(value))
        {
            error = $"{label} must be a finite number.";
            return false;
        }

        error = null;
        return true;
    }

    private bool ValidateRange(double value, string label, out string? error)
    {
        if (Minimum is { } minimum && value < minimum)
        {
            error = $"{label} must be at least the declared minimum {minimum.ToString("R", CultureInfo.InvariantCulture)}.";
            return false;
        }
        if (Maximum is { } maximum && value > maximum)
        {
            error = $"{label} must not exceed the declared maximum {maximum.ToString("R", CultureInfo.InvariantCulture)}.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseColor(string text, out string color, out string? error)
    {
        if ((text.Length == 7 || text.Length == 9)
            && text[0] == '#'
            && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
        {
            color = text.ToUpperInvariant();
            error = null;
            return true;
        }

        color = string.Empty;
        error = "Color must use #RRGGBB or #RRGGBBAA hexadecimal text.";
        return false;
    }

    private static bool TryReadFiniteJsonNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value && value.TryGetValue<double>(out number) && double.IsFinite(number)) return true;
        number = default;
        return false;
    }

    private static bool IsArrayType(string typeName) =>
        typeName.Trim().EndsWith("[]", StringComparison.Ordinal)
        || typeName.Contains("Array", StringComparison.OrdinalIgnoreCase);

    private string DraftSignature() => TemplateKind switch
    {
        "boolean" => BooleanValue?.ToString() ?? "<null>",
        "color" => ColorValue,
        "vector2" => $"{VectorX}\u001f{VectorY}",
        "vector3" => $"{VectorX}\u001f{VectorY}\u001f{VectorZ}",
        "vector4" => $"{VectorX}\u001f{VectorY}\u001f{VectorZ}\u001f{VectorW}",
        _ => TextValue
    };

    private void SetDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        ValidationMessage = TryCreateValueCore(out _, out var error) ? null : error;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsValid));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record RekallAgeStudioInspectorComponentEditorModel(
    RekallAgeInspectorComponentModel Component,
    IReadOnlyList<RekallAgeStudioInspectorPropertyEditorModel> PropertyEditors)
{
    public string Type => Component.Type;
    public string DisplayName => Component.DisplayName;
    public string? Description => Component.Description;
    public bool SchemaKnown => Component.SchemaKnown;
}
