using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;
using MipSdkDotNetQuickstart;

namespace MipSdkDotNetFileSPA.Tests;

public sealed class AuthDelegateImplementationTests
{
    private const string Tenant = "00000000-0000-0000-0000-000000000001";

    public static TheoryData<string> AuthorityChallengeHosts => new()
    {
        "login.microsoftonline.com",
        "login.windows.net",
        "login.microsoftonline.us",
        "login.microsoftonline.de",
        "login.partner.microsoftonline.cn",
        "login.sovereign.example"
    };

    public static TheoryData<string> ResourceChallengeHosts => new()
    {
        "aadrm.com",
        "api.aadrm.com",
        "api.aadrm.us",
        "api.aadrm.cn",
        "api.aadrm.de",
        "dataservice.protection.outlook.com",
        "dataservice.protection.outlook.us",
        "dataservice.protection.outlook.cn",
        "dataservice.protection.outlook.de",
        "syncservice.o365syncservice.com",
        "protection.sovereign.example"
    };

    [Theory]
    [MemberData(nameof(AuthorityChallengeHosts))]
    public void NormalizeAuthority_NormalizesSdkChallengeHost(string host)
    {
        var result = AuthDelegateImplementation.NormalizeAuthority(
            $"HTTPS://{host.ToUpperInvariant()}:443/common/",
            Tenant);

        Assert.Equal($"https://{host}/{Tenant}", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("login.microsoftonline.com/common")]
    [InlineData("http://login.microsoftonline.com/common")]
    [InlineData("https://login.microsoftonline.com:444/common")]
    [InlineData("https://user@login.microsoftonline.com/common")]
    [InlineData("https://login.microsoftonline.com/common?redirect=https://evil.example")]
    [InlineData("https://login.microsoftonline.com/common#fragment")]
    [InlineData("https://login.microsoftonline.com/common/oauth2")]
    public void NormalizeAuthority_RejectsMalformedChallenges(string authority)
    {
        Assert.Throws<InvalidOperationException>(
            () => AuthDelegateImplementation.NormalizeAuthority(authority, Tenant));
    }

    [Fact]
    public void NormalizeAuthority_UsesConfiguredTenantInsteadOfChallengeTenant()
    {
        var result = AuthDelegateImplementation.NormalizeAuthority(
            "https://login.microsoftonline.com/attacker-controlled-tenant",
            "Contoso.OnMicrosoft.com");

        Assert.Equal(
            "https://login.microsoftonline.com/contoso.onmicrosoft.com",
            result);
    }

    [Theory]
    [InlineData("tenant.rms.aadrm.com")]
    [InlineData("eu.rms.aadrm.com")]
    public void NormalizeResource_AllowsRequiredAadrmSubdomains(string host)
    {
        var result = AuthDelegateImplementation.NormalizeResource(
            $"HTTPS://{host.ToUpperInvariant()}:443/");

        Assert.Equal($"https://{host}", result);
    }

    [Theory]
    [MemberData(nameof(ResourceChallengeHosts))]
    public void NormalizeResource_NormalizesSdkChallengeHost(string host)
    {
        var result = AuthDelegateImplementation.NormalizeResource($"https://{host}/");

        Assert.Equal($"https://{host}", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.aadrm.com")]
    [InlineData("http://api.aadrm.com")]
    [InlineData("https://api.aadrm.com:444")]
    [InlineData("https://user@api.aadrm.com")]
    [InlineData("https://api.aadrm.com?resource=evil")]
    [InlineData("https://api.aadrm.com#fragment")]
    [InlineData("https://api.aadrm.com/.default")]
    [InlineData("https://api.aadrm.com/tenant")]
    public void NormalizeResource_RejectsMalformedChallenges(string resource)
    {
        Assert.Throws<InvalidOperationException>(
            () => AuthDelegateImplementation.NormalizeResource(resource));
    }

    [Fact]
    public void BuildDefaultScope_UsesNormalizedResource()
    {
        var scope = AuthDelegateImplementation.BuildDefaultScope(
            "HTTPS://API.AADRM.COM:443/");

        Assert.Equal("https://api.aadrm.com/.default", scope);
    }

    [Fact]
    public void Constructor_PreservesClientSecretMode()
    {
        var configuration = CreateConfiguration(
            useCertificate: false,
            clientSecret: "test-secret",
            certificateThumbprint: null);

        using var auth = new AuthDelegateImplementation(CreateApplicationInfo(), configuration);
        Assert.NotNull(auth);
    }

    [Fact]
    public void Settings_PreserveCertificateModeWithoutRequiringRedirectUri()
    {
        var configuration = CreateConfiguration(
            useCertificate: true,
            clientSecret: null,
            certificateThumbprint: "00112233445566778899AABBCCDDEEFF00112233");

        var settings = SampleSettings.Load(configuration, CreateApplicationInfo());
        Assert.True(settings.UseCertificate);
        Assert.Equal("00112233445566778899AABBCCDDEEFF00112233", settings.CertificateThumbprint);
    }

    private static IConfiguration CreateConfiguration(
        bool useCertificate,
        string? clientSecret,
        string? certificateThumbprint)
    {
        var values = new Dictionary<string, string?>
        {
            ["Ida:Tenant"] = Tenant,
            ["Ida:DoCertAuth"] = useCertificate.ToString(),
            ["Ida:ClientSecret"] = clientSecret,
            ["Ida:CertThumbprint"] = certificateThumbprint
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ApplicationInfo CreateApplicationInfo()
    {
        return new ApplicationInfo
        {
            ApplicationId = "00000000-0000-0000-0000-000000000002",
            ApplicationName = "Authentication tests",
            ApplicationVersion = "1.0"
        };
    }
}
