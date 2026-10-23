// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;
using MipSdkDotNetQuickstart;

namespace MipSdkDotNetFileSPA.Tests;

public sealed class SampleSettingsTests
{
    [Theory]
    [InlineData("Ida:ClientId", null)]
    [InlineData("Ida:ClientId", " ")]
    [InlineData("Ida:ClientId", "YOUR CLIENT ID")]
    [InlineData("Ida:ClientId", "not-a-guid")]
    [InlineData("Ida:ClientId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Ida:Tenant", "common")]
    [InlineData("Ida:Tenant", "organizations")]
    [InlineData("Ida:Tenant", "tenant/path")]
    [InlineData("Ida:Tenant", "contoso..com")]
    [InlineData("Ida:Tenant", "-contoso.com")]
    [InlineData("Ida:Tenant", "contoso.com.")]
    [InlineData("Ida:Tenant", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Ida:ClientSecret", "YOUR-SECRET")]
    [InlineData("Ida:ClientSecret", " ")]
    [InlineData("Ida:DoCertAuth", "typo")]
    [InlineData("App:AcceptServiceConsent", "typo")]
    [InlineData("App:Name", " ")]
    [InlineData("App:Version", null)]
    [InlineData("App:LogLevel", "Debug")]
    [InlineData("App:LogLevel", "99")]
    [InlineData("App:Cloud", "")]
    [InlineData("App:Cloud", "Unknown")]
    [InlineData("App:Cloud", "Custom")]
    [InlineData("App:Cloud", "Test")]
    [InlineData("App:Cloud", "NotACloud")]
    [InlineData("App:Cloud", "999")]
    public void Load_RejectsInvalidConfiguration(string key, string? value)
    {
        var values = TestSupport.ConfigurationValues();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => TestSupport.Settings(values));
    }

    [Theory]
    [InlineData("")]
    [InlineData("YOUR CERT THUMBPRINT")]
    [InlineData("AA")]
    [InlineData("00112233445566778899AABBCCDDEEFF0011223Z")]
    public void Load_RejectsInvalidCertificateThumbprints(string value)
    {
        var values = TestSupport.ConfigurationValues();
        values["Ida:DoCertAuth"] = "true";
        values["Ida:CertThumbprint"] = value;
        Assert.Throws<InvalidOperationException>(() => TestSupport.Settings(values));
    }

    [Fact]
    public void Load_NormalizesCertificateAndTenantAndDefaultsToWarning()
    {
        var values = TestSupport.ConfigurationValues();
        values["Ida:DoCertAuth"] = "true";
        values["Ida:CertThumbprint"] = "00 112233445566778899aabbccddeeff00112233";
        values["Ida:Tenant"] = " Contoso.OnMicrosoft.com ";
        values.Remove("Ida:ClientSecret");
        var settings = TestSupport.Settings(values);
        Assert.Equal("00112233445566778899AABBCCDDEEFF00112233", settings.CertificateThumbprint);
        Assert.Equal("contoso.onmicrosoft.com", settings.Tenant);
        Assert.Equal(LogLevel.Warning, settings.LogLevel);
        Assert.Equal(Cloud.Commercial, settings.Cloud);
        Assert.True(Path.IsPathFullyQualified(settings.CachePath));
        Assert.Equal(string.Empty, settings.ClientSecret);
    }

    [Fact]
    public void Load_PreservesSecretValueAndRequiresExplicitConsent()
    {
        var values = TestSupport.ConfigurationValues();
        values["Ida:ClientSecret"] = " secret-with-significant-spaces ";
        values.Remove("App:AcceptServiceConsent");
        var settings = TestSupport.Settings(values);
        Assert.Equal(" secret-with-significant-spaces ", settings.ClientSecret);
        Assert.False(settings.AcceptServiceConsent);
    }

    [Theory]
    [InlineData(Cloud.Commercial)]
    [InlineData(Cloud.US_GCC)]
    [InlineData(Cloud.US_GCC_High)]
    [InlineData(Cloud.US_DoD)]
    [InlineData(Cloud.China_01)]
    [InlineData(Cloud.Germany)]
    [InlineData(Cloud.US_Sec)]
    [InlineData(Cloud.US_Nat)]
    [InlineData(Cloud.Bleu)]
    public void Load_ParsesExplicitCloud(Cloud cloud)
    {
        var values = TestSupport.ConfigurationValues();
        values["App:Cloud"] = cloud.ToString().ToLowerInvariant();
        Assert.Equal(cloud, TestSupport.Settings(values).Cloud);
    }

    [Fact]
    public void Configuration_EnvironmentOverridesLocalJsonWhichOverridesDefaults()
    {
        using var directory = new TemporaryDirectory();
        System.IO.File.WriteAllText(directory.PathFor("appsettings.json"), """{"App":{"Name":"from-json","Version":"default"}}""");
        System.IO.File.WriteAllText(directory.PathFor("appsettings.local.json"), """{"App":{"Name":"from-local","Version":"local"}}""");
        var key = "MIP_App__Name";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "from-environment");
            var configuration = SampleSettings.LoadConfiguration(directory.Root, directory.Root);
            using var lifetime = configuration as IDisposable;
            Assert.Equal("from-environment", configuration["App:Name"]);
            Assert.Equal("local", configuration["App:Version"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingConsentBeforeInitializingSdk()
    {
        var values = TestSupport.ConfigurationValues();
        values["App:AcceptServiceConsent"] = "false";
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MipHandler.CreateAsync(TestSupport.Settings(values)));
        Assert.Contains("AcceptServiceConsent", exception.Message);
    }

    [Theory]
    [InlineData(true, Consent.Accept)]
    [InlineData(false, Consent.Reject)]
    public void Consent_UsesOperatorDecision(bool accepted, Consent expected) =>
        Assert.Equal(expected, new ConsentDelegateImplementation(accepted).GetUserConsent("https://api.aadrm.com"));
}
