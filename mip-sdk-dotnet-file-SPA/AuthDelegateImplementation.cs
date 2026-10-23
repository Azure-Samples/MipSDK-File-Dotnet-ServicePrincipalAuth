// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using Microsoft.InformationProtection;

namespace MipSdkDotNetQuickstart;

/// <summary>
/// Implements MIP SDK authentication using service principal credentials.
/// Supports both certificate and client secret authentication.
/// </summary>
public sealed class AuthDelegateImplementation : IAuthDelegate, IDisposable
{
    private readonly SampleSettings _settings;
    private readonly IMsalHttpClientFactory? _httpClientFactory;
    private readonly ConcurrentDictionary<string, Lazy<IConfidentialClientApplication>> _clients = new();
    private readonly Lazy<X509Certificate2> _certificate;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthDelegateImplementation"/> class.
    /// </summary>
    public AuthDelegateImplementation(ApplicationInfo appInfo, IConfiguration configuration)
        : this(SampleSettings.Load(configuration, appInfo))
    {
    }

    internal AuthDelegateImplementation(SampleSettings settings, IMsalHttpClientFactory? httpClientFactory = null)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _certificate = new Lazy<X509Certificate2>(() => LoadCertificateFromStore(settings.CertificateThumbprint));
        if (settings.UseCertificate)
        {
            _ = _certificate.Value;
        }
    }

    /// <summary>
    /// Acquires an OAuth2 access token for the specified resource.
    /// Called by the MIP SDK when authentication is required.
    /// </summary>
    public string AcquireToken(Identity identity, string authority, string resource, string claim)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scope = BuildDefaultScope(resource);
        var app = GetClient(authority);
        var tokenRequest = app.AcquireTokenForClient(scopes: [scope]);

        if (!string.IsNullOrWhiteSpace(claim))
        {
            tokenRequest = tokenRequest.WithClaims(claim);
        }

        // IAuthDelegate is synchronous even though MSAL's token API is asynchronous.
        var authResult = tokenRequest.ExecuteAsync().ConfigureAwait(false).GetAwaiter().GetResult();

        return authResult.AccessToken;
    }

    internal IConfidentialClientApplication GetClient(string authority)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedAuthority = NormalizeAuthority(authority, _settings.Tenant);
        return _clients.GetOrAdd(normalizedAuthority, value =>
            new Lazy<IConfidentialClientApplication>(() => BuildConfidentialClientApplication(value))).Value;
    }

    private IConfidentialClientApplication BuildConfidentialClientApplication(string normalizedAuthority)
    {
        var builder = ConfidentialClientApplicationBuilder.Create(_settings.ApplicationInfo.ApplicationId)
            .WithAuthority(normalizedAuthority);

        if (_settings.UseCertificate)
        {
            builder.WithCertificate(_certificate.Value);
        }
        else
        {
            builder.WithClientSecret(_settings.ClientSecret);
        }

        if (_httpClientFactory is not null)
        {
            builder.WithHttpClientFactory(_httpClientFactory);
        }

        return builder.Build();
    }

    internal static string NormalizeAuthority(string authority, string tenant)
    {
        if (string.IsNullOrWhiteSpace(authority) ||
            !Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
        {
            throw new InvalidOperationException("The authority challenge must be an absolute URI.");
        }

        if (!string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !authorityUri.IsDefaultPort)
        {
            throw new InvalidOperationException("The authority challenge must use HTTPS on the default port.");
        }

        if (!string.IsNullOrEmpty(authorityUri.UserInfo) ||
            !string.IsNullOrEmpty(authorityUri.Query) ||
            !string.IsNullOrEmpty(authorityUri.Fragment))
        {
            throw new InvalidOperationException("The authority challenge contains unsupported URI components.");
        }

        var path = authorityUri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Trim('/');
        if (path.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The authority challenge path is not supported.");
        }

        var normalizedTenant = SampleSettings.NormalizeTenant(tenant);
        var normalizedHost = authorityUri.IdnHost.ToLowerInvariant();
        return $"https://{normalizedHost}/{normalizedTenant}";
    }

    internal static string NormalizeResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource) ||
            !Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri))
        {
            throw new InvalidOperationException("The resource challenge must be an absolute URI.");
        }

        if (!string.Equals(resourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !resourceUri.IsDefaultPort)
        {
            throw new InvalidOperationException("The resource challenge must use HTTPS on the default port.");
        }

        if (!string.IsNullOrEmpty(resourceUri.UserInfo) ||
            !string.IsNullOrEmpty(resourceUri.Query) ||
            !string.IsNullOrEmpty(resourceUri.Fragment))
        {
            throw new InvalidOperationException("The resource challenge contains unsupported URI components.");
        }

        if (resourceUri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("The resource challenge path is not supported.");
        }

        var normalizedHost = resourceUri.IdnHost.ToLowerInvariant();
        return $"https://{normalizedHost}";
    }

    internal static string BuildDefaultScope(string resource)
    {
        return $"{NormalizeResource(resource)}/.default";
    }

    internal static void ValidateCertificate(X509Certificate2 certificate, DateTime now)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The configured certificate does not have an accessible private key.");
        }

        if (now < certificate.NotBefore || now > certificate.NotAfter)
        {
            throw new InvalidOperationException("The configured certificate is expired or not yet valid.");
        }
    }

    private static X509Certificate2 LoadCertificateFromStore(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        X509Certificate2? selected = null;
        try
        {
            foreach (var certificate in store.Certificates)
            {
                if (selected is null && string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    selected = certificate;
                }
                else
                {
                    certificate.Dispose();
                }
            }

            if (selected is null)
            {
                throw new InvalidOperationException("The configured certificate was not found in CurrentUser\\My.");
            }

            ValidateCertificate(selected, DateTime.Now);
            return selected;
        }
        catch
        {
            selected?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clients.Clear();
        if (_certificate.IsValueCreated)
        {
            _certificate.Value.Dispose();
        }
    }
}
