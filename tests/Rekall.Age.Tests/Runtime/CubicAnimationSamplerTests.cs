using System.Text.Json.Nodes;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Runtime;

public sealed class CubicAnimationSamplerTests
{
    public static TheoryData<string, JsonArray> InvalidKeys => new()
    {
        { "missing tangent", Keys(Key(0, 0, 0, null), Key(1, 1, 0, 0)) },
        { "non-finite tangent", Keys(Key(0, 0, 0, double.NaN), Key(1, 1, 0, 0)) },
        { "nested vector", Keys(Key(0, new JsonArray(new JsonArray(0)), new JsonArray(0), new JsonArray(0))) },
        { "mismatched vector tangent", Keys(Key(0, new JsonArray(0, 1), new JsonArray(0), new JsonArray(0, 0))) },
        { "mismatched color tangent", Keys(Key(0, "#102030", new JsonArray(0, 0, 0, 0), new JsonArray(0, 0, 0))) },
        { "duplicate time", Keys(Key(0, 0, 0, 0), Key(0, 1, 0, 0)) },
        { "decreasing time", Keys(Key(1, 0, 0, 0), Key(0, 1, 0, 0)) },
        { "string value", Keys(Key(0, "idle", 0, 0)) },
        { "inconsistent value shape", Keys(Key(0, 0, 0, 0), Key(1, new JsonArray(1), new JsonArray(0), new JsonArray(0))) },
        { "vector component limit", Keys(Key(0, Vector(Enumerable.Range(0, 17)), Vector(Enumerable.Repeat(0, 17)), Vector(Enumerable.Repeat(0, 17)))) }
    };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public void ParserRejectsMalformedCubicKeysWithoutPartialResults(string _, JsonArray nodes)
    {
        var accepted = RekallAgeCubicAnimationSampler.TryCreateKeys(nodes, out var keys, out var issue);

        Assert.False(accepted);
        Assert.Empty(keys);
        Assert.NotEmpty(issue);
    }

    [Fact]
    public void SamplerPreservesExactEndpointsAndClampsColorChannels()
    {
        var nodes = Keys(
            Key(0, "#102030ff", new JsonArray(0, 0, 0, 0), new JsonArray(4096, -4096, 0, 0)),
            Key(1, "#50607080", new JsonArray(0, 0, 0, 0), new JsonArray(0, 0, 0, 0)));
        Assert.True(RekallAgeCubicAnimationSampler.TryCreateKeys(nodes, out var keys, out var issue), issue);

        Assert.Equal("\"#102030ff\"", RekallAgeCubicAnimationSampler.Sample(keys, -1)!.ToJsonString());
        Assert.Equal("\"#ff0050c0\"", RekallAgeCubicAnimationSampler.Sample(keys, 0.5)!.ToJsonString());
        Assert.Equal("\"#50607080\"", RekallAgeCubicAnimationSampler.Sample(keys, 1)!.ToJsonString());
    }

    private static JsonArray Keys(params JsonObject[] keys) => new(keys.Cast<JsonNode?>().ToArray());

    private static JsonArray Vector(IEnumerable<int> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonObject Key(double time, JsonNode? value, JsonNode? inTangent, JsonNode? outTangent) => new()
    {
        ["time"] = time,
        ["value"] = value,
        ["inTangent"] = inTangent,
        ["outTangent"] = outTangent
    };
}
