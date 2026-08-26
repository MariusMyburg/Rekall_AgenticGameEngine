using System.Text.RegularExpressions;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class OpenAiToolNameMapTests
{
    [Fact]
    public void CanonicalNamesMapToReadableHashedProviderAliases()
    {
        var map = RekallAgeOpenAiToolNameMap.Create(
        [
            "rekall.context.engine_status",
            "rekall_context.engine_status",
            "Rekall.Context.Engine_Status"
        ]);

        Assert.Equal(
        [
            "rekall_context_engine_status_8179b61222fc",
            "rekall_context_engine_status_44547da675b8",
            "Rekall_Context_Engine_Status_3e165c4fa346"
        ],
            map.Aliases);
        Assert.All(map.Aliases, alias => Assert.Matches("^[A-Za-z0-9_-]{1,64}$", alias));
    }

    [Fact]
    public void LongCanonicalNameKeepsHashWithinProviderLimit()
    {
        const string canonical =
            "rekall.very.long.namespace.with.an.excessively.long.tool.name.that.must.be.truncated.before.the.hash";
        var map = RekallAgeOpenAiToolNameMap.Create([canonical]);

        var alias = Assert.Single(map.Aliases);
        Assert.Equal(
            "rekall_very_long_namespace_with_an_excessively_long_b5192807456e",
            alias);
        Assert.Equal(64, alias.Length);
        Assert.Matches(new Regex("_[0-9a-f]{12}$", RegexOptions.CultureInvariant), alias);
    }

    [Fact]
    public void SanitizationCollisionsReverseToTheirExactCanonicalNames()
    {
        var map = RekallAgeOpenAiToolNameMap.Create(["a.b", "a_b"]);

        Assert.Equal(["a_b_2e7336dc8eba", "a_b_648fa9b31bc7"], map.Aliases);
        Assert.Equal("a.b", map.ToCanonical("a_b_2e7336dc8eba"));
        Assert.Equal("a_b", map.ToCanonical("a_b_648fa9b31bc7"));
        Assert.Equal("a_b_2e7336dc8eba", map.ToAlias("a.b"));
        Assert.Equal("a_b_648fa9b31bc7", map.ToAlias("a_b"));
    }

    [Fact]
    public void AliasEnumerationPreservesInputOrdering()
    {
        var map = RekallAgeOpenAiToolNameMap.Create(
            ["rekall.scene.inspect", "rekall.context.engine_status"]);

        Assert.Equal(
        [
            "rekall_scene_inspect_d7c351b75103",
            "rekall_context_engine_status_8179b61222fc"
        ],
            map.Aliases);
    }

    [Fact]
    public void DuplicateCanonicalNamesAreRejectedDeterministically()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            RekallAgeOpenAiToolNameMap.Create(["rekall.scene.inspect", "rekall.scene.inspect"]));

        Assert.Equal("canonicalNames", error.ParamName);
    }
}
