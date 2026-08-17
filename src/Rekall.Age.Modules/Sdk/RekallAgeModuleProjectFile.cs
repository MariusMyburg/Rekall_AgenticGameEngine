using System.Security;
using Rekall.Age.Core.Product;

namespace Rekall.Age.Modules.Sdk;

public static class RekallAgeModuleProjectFile
{
    public static string Create(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        var escapedModuleName = SecurityElement.Escape(moduleName);
        var compatibilityVersion = RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion;
        var sdkProps = $"..\\..\\.rekall\\sdk\\{compatibilityVersion}\\Rekall.Age.Sdk.props";
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{escapedModuleName}</AssemblyName>
                <RekallAgeSdkCompatibilityVersion>{compatibilityVersion}</RekallAgeSdkCompatibilityVersion>
              </PropertyGroup>
              <Import Project="{sdkProps}" Condition="Exists('{sdkProps}')" />
              <Target Name="ValidateRekallAgeSdk" BeforeTargets="ResolveReferences" Condition="!Exists('{sdkProps}')">
                <Error Code="REKALL_SDK_MISSING" Text="Rekall AGE module SDK compatibility version {compatibilityVersion} is missing. Re-run a Rekall AGE module scaffold or SDK repair command." />
              </Target>
            </Project>

            """;
    }
}
