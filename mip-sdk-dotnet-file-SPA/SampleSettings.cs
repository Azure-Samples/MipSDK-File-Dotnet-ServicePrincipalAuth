// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;

namespace MipSdkDotNetQuickstart;

public sealed class SampleSettings
{
    public required ApplicationInfo ApplicationInfo { get; init; }
    public required string Tenant { get; init; }
    public bool UseCertificate { get; init; }
    public string CertificateThumbprint { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public required string CachePath { get; init; }
    public Cloud Cloud { get; init; } = Cloud.Commercial;
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;
    public bool AcceptServiceConsent { get; init; }

    public static IConfigurationRoot LoadConfiguration(string? basePath = null) =>
        LoadConfiguration(basePath ?? AppContext.BaseDirectory, Environment.CurrentDirectory);

    internal static IConfigurationRoot LoadConfiguration(string basePath, string workingDirectory)
    {
        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(Path.Combine(workingDirectory, "appsettings.local.json"), optional: true)
            .AddEnvironmentVariables("MIP_")
            .Build();
    }

    public static SampleSettings Load(IConfiguration configuration, ApplicationInfo? applicationInfo = null)
    {
        var clientId = applicationInfo?.ApplicationId ?? Required(configuration, "Ida:ClientId");
        if (!Guid.TryParse(clientId, out var clientGuid) || clientGuid == Guid.Empty)
        {
            throw new InvalidOperationException("'Ida:ClientId' must be a nonempty application GUID.");
        }

        var tenant = NormalizeTenant(Required(configuration, "Ida:Tenant"));
        var cloud = Cloud.Commercial;
        var configuredCloud = configuration["App:Cloud"];
        if (configuredCloud is not null &&
            (!Enum.TryParse(configuredCloud, ignoreCase: true, out cloud) ||
             !Enum.IsDefined(cloud) || cloud is Cloud.Unknown or Cloud.Custom or Cloud.Test))
        {
            throw new InvalidOperationException("'App:Cloud' must name a supported Azure cloud, such as Commercial, US_GCC, US_GCC_High, US_DoD, or China_01.");
        }

        var certificateMode = ReadBoolean(configuration, "Ida:DoCertAuth", defaultValue: true);
        var thumbprint = string.Empty;
        var secret = string.Empty;
        if (certificateMode)
        {
            thumbprint = string.Concat(Required(configuration, "Ida:CertThumbprint")
                .Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
            if (thumbprint.Length != 40 || thumbprint.Any(character => !char.IsAsciiHexDigit(character)))
            {
                throw new InvalidOperationException("'Ida:CertThumbprint' must contain 40 hexadecimal characters.");
            }
        }
        else
        {
            secret = Required(configuration, "Ida:ClientSecret", trim: false);
        }

        var logLevel = LogLevel.Warning;
        var configuredLogLevel = configuration["App:LogLevel"];
        if (configuredLogLevel is not null &&
            (!Enum.TryParse(configuredLogLevel, ignoreCase: true, out logLevel) || !Enum.IsDefined(logLevel)))
        {
            throw new InvalidOperationException("'App:LogLevel' must be Trace, Info, Warning, or Error.");
        }

        var cachePath = configuration["App:CachePath"];
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MipSdkSample", clientGuid.ToString("D"), tenant);
        }

        return new SampleSettings
        {
            ApplicationInfo = new ApplicationInfo
            {
                ApplicationId = clientGuid.ToString("D"),
                ApplicationName = applicationInfo?.ApplicationName ?? Required(configuration, "App:Name"),
                ApplicationVersion = applicationInfo?.ApplicationVersion ?? Required(configuration, "App:Version")
            },
            Tenant = tenant,
            Cloud = cloud,
            UseCertificate = certificateMode,
            CertificateThumbprint = thumbprint,
            ClientSecret = secret,
            CachePath = Path.GetFullPath(cachePath),
            LogLevel = logLevel,
            AcceptServiceConsent = ReadBoolean(configuration, "App:AcceptServiceConsent", defaultValue: false)
        };
    }

    internal static string NormalizeTenant(string tenant)
    {
        var value = tenant.Trim().ToLowerInvariant();
        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            return id.ToString("D");
        }

        if (value.Contains('.', StringComparison.Ordinal) &&
            value.Length <= 253 &&
            value.Split('.').All(part => part.Length is > 0 and <= 63 &&
                char.IsAsciiLetterOrDigit(part[0]) && char.IsAsciiLetterOrDigit(part[^1]) &&
                part.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) &&
            !Guid.TryParse(value, out _))
        {
            return value;
        }

        throw new InvalidOperationException("'Ida:Tenant' must be a tenant GUID or verified DNS domain, not common or organizations.");
    }

    private static bool ReadBoolean(IConfiguration configuration, string key, bool defaultValue)
    {
        var value = configuration[key];
        if (value is null)
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var result)
            ? result
            : throw new InvalidOperationException($"'{key}' must be true or false.");
    }

    private static string Required(IConfiguration configuration, string key, bool trim = true)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) ||
            value.TrimStart().StartsWith("YOUR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Configuration '{key}' is required and must not be a placeholder.");
        }

        return trim ? value.Trim() : value;
    }
}
