// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Identity.Client;
using Microsoft.InformationProtection;
using MipSdkDotNetQuickstart;

namespace MipSdkDotNetFileSPA.Tests;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public void Client_IsReusedForNormalizedAuthority()
    {
        using var auth = new AuthDelegateImplementation(TestSupport.Settings());
        Assert.Same(
            auth.GetClient("https://login.microsoftonline.com/common"),
            auth.GetClient("HTTPS://LOGIN.MICROSOFTONLINE.COM:443/organizations/"));
        Assert.NotSame(
            auth.GetClient("https://login.microsoftonline.com/common"),
            auth.GetClient("https://login.microsoftonline.us/common"));
    }

    [Fact]
    public void AcquireToken_UsesAppCacheAndConfiguredTenant()
    {
        using var transport = new TokenTransport();
        using var auth = new AuthDelegateImplementation(TestSupport.Settings(), transport);
        var first = auth.AcquireToken(new Identity(string.Empty), "https://login.microsoftonline.com/common", "https://api.aadrm.com/", "");
        var second = auth.AcquireToken(new Identity(string.Empty), "https://login.microsoftonline.com/organizations", "https://api.aadrm.com", "");
        Assert.Equal("unit-test-access-token", first);
        Assert.Equal(first, second);
        var request = Assert.Single(transport.TokenRequests);
        Assert.Contains(TestSupport.Tenant, request.Path);
        Assert.Equal("https://api.aadrm.com/.default", request.Body["scope"]);
        Assert.Equal("client_credentials", request.Body["grant_type"]);
    }

    [Fact]
    public void AcquireToken_ForwardsClaimsAndRejectsMalformedResourcesBeforeHttp()
    {
        using var transport = new TokenTransport();
        using var auth = new AuthDelegateImplementation(TestSupport.Settings(), transport);
        const string claims = """{"access_token":{"test":{"essential":true}}}""";
        auth.AcquireToken(new Identity(string.Empty), "https://login.microsoftonline.com/common", "https://api.aadrm.com", claims);
        Assert.Equal(claims, Assert.Single(transport.TokenRequests).Body["claims"]);
        Assert.Throws<InvalidOperationException>(() =>
            auth.AcquireToken(new Identity(string.Empty), "https://login.microsoftonline.com/common", "http://api.aadrm.com", ""));
        Assert.Single(transport.TokenRequests);
    }

    [Fact]
    public void DisposedDelegate_RejectsReuse()
    {
        var auth = new AuthDelegateImplementation(TestSupport.Settings());
        auth.Dispose();
        auth.Dispose();
        Assert.Throws<ObjectDisposedException>(() => auth.GetClient("https://login.microsoftonline.com/common"));
    }

    [Fact]
    public void CertificateValidation_RejectsPublicOnlyExpiredAndFutureCertificates()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=MIP SDK unit test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
        AuthDelegateImplementation.ValidateCertificate(certificate, DateTime.Now);
        Assert.Throws<InvalidOperationException>(() =>
            AuthDelegateImplementation.ValidateCertificate(certificate, now.AddDays(2).LocalDateTime));
        Assert.Throws<InvalidOperationException>(() =>
            AuthDelegateImplementation.ValidateCertificate(certificate, now.AddDays(-2).LocalDateTime));
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        Assert.Throws<InvalidOperationException>(() =>
            AuthDelegateImplementation.ValidateCertificate(publicOnly, DateTime.Now));
    }

    [Fact]
    public void MissingCertificate_FailsBeforeAnyNetworkRequest()
    {
        var values = TestSupport.ConfigurationValues();
        values["Ida:DoCertAuth"] = "true";
        values["Ida:CertThumbprint"] = new string('0', 40);
        var exception = Assert.Throws<InvalidOperationException>(
            () => new AuthDelegateImplementation(TestSupport.Settings(values)));
        Assert.Contains("not found", exception.Message);
    }

    private sealed class TokenTransport : HttpMessageHandler, IMsalHttpClientFactory
    {
        private readonly HttpClient _client;
        internal ConcurrentQueue<(string Path, Dictionary<string, string> Body)> TokenRequests { get; } = new();

        internal TokenTransport() => _client = new HttpClient(this, disposeHandler: false);
        public HttpClient GetHttpClient() => _client;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string response;
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                var values = body.Split('&').Select(part => part.Split('=', 2))
                    .ToDictionary(parts => Decode(parts[0]), parts => Decode(parts[1]));
                TokenRequests.Enqueue((request.RequestUri.AbsolutePath, values));
                response = """{"access_token":"unit-test-access-token","token_type":"Bearer","expires_in":3600,"ext_expires_in":3600}""";
            }
            else if (request.RequestUri!.AbsolutePath.Contains("/discovery/instance", StringComparison.Ordinal))
            {
                response = $$"""
                    {"tenant_discovery_endpoint":"https://login.microsoftonline.com/{{TestSupport.Tenant}}/v2.0/.well-known/openid-configuration",
                     "metadata":[{"preferred_network":"login.microsoftonline.com","preferred_cache":"login.microsoftonline.com",
                     "aliases":["login.microsoftonline.com","login.windows.net"]}]}
                    """;
            }
            else
            {
                throw new InvalidOperationException("Unexpected HTTP request in offline authentication test.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _client.Dispose(); }
            base.Dispose(disposing);
        }

        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
    }
}
