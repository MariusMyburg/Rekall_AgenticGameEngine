using Rekall.Age.Core.Product;

namespace Rekall.Age.Tests.Core;

public sealed class ProductInfoTests
{
    [Fact]
    public void ProductMetadataDefinesPreviewCompatibilityAndCapabilityStability()
    {
        var product = RekallAgeProductInfo.Current;

        Assert.Equal("Rekall AGE", product.Name);
        Assert.Equal("0.1.0-preview.1", product.Version);
        Assert.Equal("preview", product.Channel);
        Assert.Equal(1, product.ProjectSchemaVersion);
        Assert.Equal(1, product.ModuleSdkCompatibilityVersion);
        Assert.True(product.Proprietary);
        Assert.Equal("supported", RekallAgeProductInfo.Capability("authoring.core").Stability);
        Assert.Equal("experimental", RekallAgeProductInfo.Capability("runtime.openxr").Stability);
        Assert.Equal("experimental", RekallAgeProductInfo.Capability("runtime.multiplayer").Stability);
    }
}
