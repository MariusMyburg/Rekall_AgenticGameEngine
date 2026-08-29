using System.Globalization;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioInspectorPropertyEditorModelTests
{
    [Theory]
    [InlineData("number", "12.5", "12.5")]
    [InlineData("integer", "12", "12")]
    [InlineData("boolean", "true", "true")]
    public void TypedScalarProducesNativeJson(string kind, string input, string expectedJson)
    {
        var row = Row(kind, input);

        Assert.True(row.TryCreateValue(out var value, out var error), error);
        Assert.Equal(expectedJson, value!.ToJsonString());
    }

    [Theory]
    [InlineData("line one\\nline two", "line one\nline two")]
    [InlineData("say \\\"hello\\\"", "say \"hello\"")]
    [InlineData("folder\\\\child", "folder\\child")]
    public void StringRowDecodesInspectorJsonEscapesAndRestoresTheNativeValue(
        string inspectorDisplay,
        string nativeValue)
    {
        var row = Row("string", inspectorDisplay);

        Assert.Equal(nativeValue, row.TextValue);
        Assert.True(row.TryCreateValue(out var value, out var error), error);
        Assert.Equal(nativeValue, value!.GetValue<string>());

        row.TextValue = "temporary edit";
        row.AcceptPersistedValue(Property("string", inspectorDisplay));
        Assert.Equal(nativeValue, row.TextValue);
        row.TextValue = "another temporary edit";
        row.RestoreOriginalDraft();
        Assert.Equal(nativeValue, row.TextValue);
        Assert.True(row.TryCreateValue(out value, out error), error);
        Assert.Equal(nativeValue, value!.GetValue<string>());
    }

    [Fact]
    public void NumericParsingUsesInvariantCultureAndRejectsFractionsNonFiniteValuesAndRanges()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var number = Row("number", "1", minimum: 0, maximum: 20);
            number.TextValue = "12.5";
            Assert.True(number.TryCreateValue(out var value, out var error), error);
            Assert.Equal("12.5", value!.ToJsonString());

            number.TextValue = "12,5";
            Assert.False(number.TryCreateValue(out _, out _));
            number.TextValue = "NaN";
            Assert.False(number.TryCreateValue(out _, out _));
            number.TextValue = "Infinity";
            Assert.False(number.TryCreateValue(out _, out _));
            number.TextValue = "21";
            Assert.False(number.TryCreateValue(out _, out var rangeError));
            Assert.Contains("maximum", rangeError, StringComparison.OrdinalIgnoreCase);

            var integer = Row("integer", "1");
            integer.TextValue = "1.5";
            Assert.False(integer.TryCreateValue(out _, out var integerError));
            Assert.Contains("integer", integerError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void MetadataSelectsExplicitTemplatesBeforeEnumAndUnknownKindsFallBackToJson()
    {
        Assert.Equal("assetRef", Row("assetRef", "texture-id", allowedValues: ["other"]).TemplateKind);
        Assert.Equal("entityRef", Row("entityRef", "entity-id", allowedValues: ["other"]).TemplateKind);
        Assert.Equal("enum", Row("string", "loop", allowedValues: ["loop", "clamp"]).TemplateKind);
        Assert.Equal("json", Row("animationTracks", "[]", typeName: "Object[]").TemplateKind);
        Assert.Equal("json", Row("futureStructuredKind", "{}").TemplateKind);
        Assert.Equal("json", Row("string", "[]", typeName: "String[]").TemplateKind);
        Assert.Equal("string", Row("string", "plain").TemplateKind);
    }

    [Fact]
    public void AssetAndEntityRowsExposeOnlyTheirTypedChoicesAndEntityValuesStayStable()
    {
        var asset = Row("assetRef", "audio/old", assetKind: "audio", assetChoices: ["audio/one", "audio/two"]);
        Assert.Equal(["audio/one", "audio/two"], asset.Choices);

        var entity = Row(
            "entityRef",
            "entity-2",
            entityChoices:
            [
                new("Player (entity-1)", "entity-1"),
                new("Target (entity-2)", "entity-2")
            ]);
        Assert.Equal(["entity-1", "entity-2"], entity.Choices);
        Assert.Equal("Target (entity-2)", entity.ChoiceItems[1].DisplayName);
        Assert.Equal("entity-2", entity.ReferenceValue);
        Assert.Equal("Target (entity-2)", entity.ReferenceSearchText);
        entity.TextValue = entity.ChoiceItems[0].Value;
        Assert.True(entity.TryCreateValue(out var value, out var error), error);
        Assert.Equal("\"entity-1\"", value!.ToJsonString());

        entity.RestoreOriginalDraft();
        entity.ReferenceSearchText = "agent-authored-stable-id";
        Assert.Equal("entity-2", entity.ReferenceValue);
        Assert.False(entity.IsDirty);
        entity.AcceptReferenceSearchText();
        Assert.Equal("agent-authored-stable-id", entity.ReferenceValue);
        Assert.True(entity.IsDirty);
        Assert.True(entity.TryCreateValue(out value, out error), error);
        Assert.Equal("\"agent-authored-stable-id\"", value!.ToJsonString());
    }

    [Fact]
    public void ColorRowNormalizesAHexColorAndRejectsInvalidHex()
    {
        var row = Row("color", "#112233");
        row.ColorValue = "#ff66ccff";

        Assert.True(row.TryCreateValue(out var value, out var error), error);
        Assert.Equal("#FF66CCFF", row.ColorValue);
        Assert.Equal("\"#FF66CCFF\"", value!.ToJsonString());

        row.ColorValue = "#abcd";
        Assert.False(row.TryCreateValue(out _, out var invalidError));
        Assert.Contains("#RRGGBB", invalidError, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorChannelDraftsProduceCanonicalColorAndRetainInvalidChannelFeedback()
    {
        var row = Row("color", "#10203040");
        var swatchNotifications = 0;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RekallAgeStudioInspectorPropertyEditorModel.ColorValue))
            {
                swatchNotifications++;
            }
        };

        Assert.Equal("16", row.ColorRed);
        Assert.Equal("32", row.ColorGreen);
        Assert.Equal("48", row.ColorBlue);
        Assert.Equal("64", row.ColorAlpha);

        row.ColorRed = "17";
        Assert.Equal("#11203040", row.ColorValue);
        Assert.Equal(1, swatchNotifications);

        row.ColorRed = "255";
        row.ColorGreen = "102";
        row.ColorBlue = "204";
        row.ColorAlpha = "255";
        Assert.True(row.TryCreateValue(out var value, out var error), error);
        Assert.Equal("\"#FF66CCFF\"", value!.ToJsonString());

        row.ColorRed = "256";
        Assert.False(row.TryCreateValue(out _, out var invalidError));
        Assert.Contains("0 and 255", invalidError, StringComparison.Ordinal);
        Assert.Equal("256", row.ColorRed);
    }

    [Fact]
    public void Vector3RowBuildsNativeArrayAndRejectsNaNWrongArityAndRangeViolations()
    {
        var row = Row("vector3", "[0,0,0]", minimum: -10, maximum: 10);
        row.VectorX = "1";
        row.VectorY = "2";
        row.VectorZ = "3";

        Assert.True(row.TryCreateValue(out var value, out var error), error);
        Assert.Equal("[1,2,3]", value!.ToJsonString());

        row.VectorX = "NaN";
        Assert.False(row.TryCreateValue(out _, out _));
        row.VectorX = "11";
        Assert.False(row.TryCreateValue(out _, out var rangeError));
        Assert.Contains("maximum", rangeError, StringComparison.OrdinalIgnoreCase);

        var wrongArity = Row("vector3", "[1,2]");
        Assert.False(wrongArity.TryCreateValue(out _, out var arityError));
        Assert.Contains("3", arityError, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAndStructuredKindsRequireValidJsonAndPreserveNativeShapes()
    {
        var structured = Row("animationTracks", "[]", typeName: "Object[]");
        structured.TextValue = "{\"track\":[1,true]}";
        Assert.True(structured.TryCreateValue(out var value, out var error), error);
        Assert.Equal("{\"track\":[1,true]}", value!.ToJsonString());

        structured.TextValue = "not-json";
        Assert.False(structured.TryCreateValue(out _, out var invalidError));
        Assert.Contains("JSON", invalidError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DirtyComparisonIsSemanticAndRestoreAndAcceptUseThePersistedDraft()
    {
        var number = Row("number", "12.5");
        number.TextValue = "12.50";
        Assert.False(number.IsDirty);
        number.TextValue = "13";
        Assert.True(number.IsDirty);
        number.RestoreOriginalDraft();
        Assert.Equal("12.5", number.TextValue);
        Assert.False(number.IsDirty);

        var json = Row("json", "{\"a\":1,\"b\":2}", typeName: "Object");
        json.TextValue = "{ \"b\" : 2, \"a\" : 1 }";
        Assert.False(json.IsDirty);
        json.TextValue = "{\"a\":2}";
        Assert.True(json.IsDirty);
        json.AcceptPersistedValue(Property("json", "{\"a\":2}", typeName: "Object"));
        Assert.Equal("{\"a\":2}", json.OriginalDisplayValue);
        Assert.False(json.IsDirty);
    }

    [Fact]
    public void UndefinedPropertyStartsUnsetAndCleanWithAnEmptyTypeAppropriateDraft()
    {
        var property = Property("object", string.Empty, typeName: "Object") with { IsDefined = false };
        var row = new RekallAgeStudioInspectorPropertyEditorModel("Game.State", property);

        Assert.False(row.IsDefined);
        Assert.Equal("{}", row.TextValue);
        Assert.False(row.IsDirty);
        row.TextValue = "{\"ready\":true}";
        Assert.True(row.IsDirty);
        row.RestoreOriginalDraft();
        Assert.Equal("{}", row.TextValue);
        Assert.False(row.IsDirty);
    }

    private static RekallAgeStudioInspectorPropertyEditorModel Row(
        string kind,
        string value,
        string? typeName = null,
        string? assetKind = null,
        double? minimum = null,
        double? maximum = null,
        IReadOnlyList<string>? allowedValues = null,
        IReadOnlyList<string>? assetChoices = null,
        IReadOnlyList<RekallAgeStudioInspectorPropertyChoice>? entityChoices = null) =>
        new(
            "Game.State",
            Property(kind, value, typeName, assetKind, minimum, maximum, allowedValues),
            assetChoices,
            entityChoices);

    private static RekallAgeInspectorPropertyModel Property(
        string kind,
        string value,
        string? typeName = null,
        string? assetKind = null,
        double? minimum = null,
        double? maximum = null,
        IReadOnlyList<string>? allowedValues = null) =>
        new("value", value, kind)
        {
            TypeName = typeName ?? kind,
            EditorKind = kind,
            AssetKind = assetKind,
            Minimum = minimum,
            Maximum = maximum,
            AllowedValues = allowedValues ?? []
        };
}
