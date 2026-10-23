// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;

namespace MipSdkDotNetQuickstart;

public sealed record LabelInfo(
    string Id, string Name, bool IsActive, bool IsProtected, IReadOnlyList<LabelInfo> Children);

public sealed record FileLabelInfo(string Id, string Name, bool IsProtected);

public enum LabelResult { Applied, Unchanged }

/// <summary>A single-tenant File SDK session. Finish operations before disposing it.</summary>
public sealed class MipHandler : IDisposable
{
    private static readonly Lazy<bool> SdkInitialized = new(() =>
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("This sample requires Windows and a 64-bit process.");
        }

        MIP.Initialize(MipComponent.File);
        return true;
    });

    private readonly IFileEngine _engine;
    private readonly IFileProfile? _profile;
    private readonly Action? _releaseContext;
    private bool _disposed;

    internal MipHandler(IFileEngine engine, IFileProfile? profile = null, Action? releaseContext = null)
    {
        _engine = engine;
        _profile = profile;
        _releaseContext = releaseContext;
    }

    public static async Task<MipHandler> CreateAsync(SampleSettings settings)
    {
        if (!settings.AcceptServiceConsent)
        {
            throw new InvalidOperationException(
                "Review the service-consent notice in README.md and set 'App:AcceptServiceConsent' to true.");
        }

        var auth = new AuthDelegateImplementation(settings);
        MipContext? context = null;
        try
        {
            InitializeSdk();
            Directory.CreateDirectory(settings.CachePath);
            var mipConfiguration = new MipConfiguration(
                settings.ApplicationInfo, settings.CachePath, settings.LogLevel, false, CacheStorageType.OnDiskEncrypted)
            {
                // Offline publishing is enabled by default. A known service-principal bug
                // requires super-user privileges to fetch its full RMS templates.
                // To use online publishing without elevating permissions, uncomment:
                // FeatureSettingsOverride = new Dictionary<FlightingFeature, bool>
                // {
                //     [FlightingFeature.FileSDKOfflinePublishing] = false
                // },
                DiagnosticOverride = new DiagnosticConfiguration
                {
                    IsMinimalTelemetryEnabled = false,
                    IsTraceLoggingEnabled = false
                }
            };
            context = MIP.CreateMipContext(mipConfiguration);
        }
        catch
        {
            auth.Dispose();
            throw;
        }

        return await CreateAsync(
            () => MIP.LoadFileProfileAsync(new FileProfileSettings(
                context, CacheStorageType.OnDiskEncrypted, new ConsentDelegateImplementation(settings.AcceptServiceConsent))),
            profile => profile.AddEngineAsync(CreateEngineSettings(settings, auth)),
            () =>
            {
                try { context.ShutDown(); }
                finally
                {
                    try { context.Dispose(); }
                    finally { auth.Dispose(); }
                }
            }).ConfigureAwait(false);
    }

    internal static void InitializeSdk() => _ = SdkInitialized.Value;

    internal static FileEngineSettings CreateEngineSettings(SampleSettings settings, IAuthDelegate auth) =>
        new($"{settings.ApplicationInfo.ApplicationId}@{settings.Tenant}", auth, string.Empty, "en-US")
        {
            Cloud = settings.Cloud
        };

    internal static async Task<MipHandler> CreateAsync(
        Func<Task<IFileProfile>> loadProfile,
        Func<IFileProfile, Task<IFileEngine>> addEngine,
        Action releaseContext)
    {
        IFileProfile? profile = null;
        try
        {
            profile = await loadProfile().ConfigureAwait(false);
            var engine = await addEngine(profile).ConfigureAwait(false);
            return new MipHandler(engine, profile, releaseContext);
        }
        catch (Exception initializationError)
        {
            try { ReleaseResources(null, profile, releaseContext); }
            catch (Exception cleanupError) { initializationError.Data["CleanupError"] = cleanupError; }
            throw;
        }
    }

    public IReadOnlyList<LabelInfo> ListLabels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _engine.SensitivityLabels.Select(Snapshot).ToArray();
    }

    public async Task<FileLabelInfo?> GetLabelAsync(string inputPath, bool auditDiscovery = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = ValidateInputPath(inputPath);
        using var handler = await _engine.CreateFileHandlerAsync(
            input, input, auditDiscovery, null, auditDiscovery).ConfigureAwait(false);
        var contentLabel = handler.Label;
        return contentLabel is null
            ? null
            : new FileLabelInfo(contentLabel.Label.Id, contentLabel.Label.Name, contentLabel.IsProtectionAppliedFromLabel);
    }

    public async Task<LabelResult> SetLabelAsync(
        string inputPath, string outputPath, string labelId, bool audit = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = ValidateInputPath(inputPath);
        var output = ValidateOutputPath(input, outputPath);
        if (!Guid.TryParse(labelId, out var parsedId) || parsedId == Guid.Empty)
        {
            throw new ArgumentException("The label ID must be a nonempty GUID.", nameof(labelId));
        }

        var label = _engine.GetLabelById(parsedId.ToString("D"))
            ?? throw new ArgumentException("The label ID is not available to this application.", nameof(labelId));
        if (!label.IsActive || label.Children.Count != 0)
        {
            throw new ArgumentException("Choose an active leaf label, not an inactive label or a parent label.", nameof(labelId));
        }

        using var handler = await _engine.CreateFileHandlerAsync(input, input, audit, null, false).ConfigureAwait(false);
        handler.SetLabel(label, new LabelingOptions { AssignmentMethod = AssignmentMethod.Standard }, new ProtectionSettings());
        if (!handler.IsModified())
        {
            return LabelResult.Unchanged;
        }

        var stagingPath = Path.Combine(Path.GetDirectoryName(output)!,
            $".mip-{Guid.NewGuid():N}{Path.GetExtension(output)}");
        try
        {
            if (!await handler.CommitAsync(stagingPath).ConfigureAwait(false))
            {
                throw new IOException("The MIP SDK could not commit the labeled file.");
            }

            // A same-directory move publishes the result without overwriting an existing file, including races.
            System.IO.File.Move(stagingPath, output, overwrite: false);
            var actual = await GetLabelAsync(output).ConfigureAwait(false);
            if (actual is null || !string.Equals(actual.Id, label.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The output label did not match the requested label. The output was retained for inspection.");
            }

            if (label.HasRightsManagementPolicy && !actual.IsProtected)
            {
                throw new IOException("The output label requires protection, but label-applied protection was not found. The output was retained for inspection.");
            }

            if (audit)
            {
                handler.NotifyCommitSuccessful(output);
            }

            return LabelResult.Applied;
        }
        finally
        {
            if (System.IO.File.Exists(stagingPath))
            {
                System.IO.File.Delete(stagingPath);
            }
        }
    }

    internal static string ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An input file path is required.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!System.IO.File.Exists(fullPath))
        {
            throw new FileNotFoundException("The input file does not exist.", fullPath);
        }

        return fullPath;
    }

    internal static string ValidateOutputPath(string input, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output file path is required.", nameof(path));
        }

        var output = Path.GetFullPath(path.Trim().Trim('"'));
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Input and output paths must be different.", nameof(path));
        }

        if (System.IO.File.Exists(output) || Directory.Exists(output))
        {
            throw new IOException("The output path already exists. Choose a new file name.");
        }

        if (!Directory.Exists(Path.GetDirectoryName(output)))
        {
            throw new DirectoryNotFoundException("The output directory must already exist.");
        }

        return output;
    }

    private static LabelInfo Snapshot(Label label) => new(
        label.Id, label.Name, label.IsActive,
        label.HasRightsManagementPolicy || label.HasAdhocProtection || label.HasDoNotForwardProtection || label.HasEncryptOnlyProtection,
        label.Children.Select(Snapshot).ToArray());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseResources(_engine, _profile, _releaseContext);
    }

    private static void ReleaseResources(IFileEngine? engine, IFileProfile? profile, Action? releaseContext)
    {
        try { engine?.Dispose(); }
        finally
        {
            // Disposing the profile releases its engines; a separate unload is unnecessary.
            try { profile?.Dispose(); }
            finally { releaseContext?.Invoke(); }
        }
    }
}
