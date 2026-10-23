# MIP SDK File API sample: service principal authentication

A Windows x64, .NET 10 console sample that lists Microsoft Purview sensitivity labels,
reads a file's label, and applies a label to a new output file. Authentication uses
Microsoft Entra ID client credentials, with a certificate or client secret. It does
not sign in a user.

This is a single-tenant sample, not a production document-processing service.
Managed identity, delegated-user labeling, custom permissions, label removal,
justified downgrades, and automatic classification are not implemented.

## Prerequisites and package baseline

- Supported Windows x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
  `global.json` selects a stable .NET 10 SDK feature band.
- The current supported
  [Microsoft Visual C++ x64 Redistributable](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist).
  The NuGet package supplies the MIP native DLLs; keep its `x64` subdirectory intact.
- For live operations: a licensed, configured Microsoft Purview Information Protection
  tenant, file-scoped sensitivity labels, and an Entra application with admin-consented
  application permissions. No tenant credentials are needed for the offline tests.

Stable application dependencies verified on **October 22, 2026**:

| Package | Version |
| --- | --- |
| `Microsoft.InformationProtection.File` | `1.18.148` |
| `Microsoft.Identity.Client` (MSAL.NET) | `4.91.1` |
| `Microsoft.Extensions.Configuration.Json` | `10.0.13` |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | `10.0.13` |

Tests use xUnit v3 `4.0.2`, Moq `4.21.0`, and Coverlet MTP `10.1.0`.
The .NET 10 test runner is **Microsoft.Testing.Platform**, configured in `global.json`.

## Register the application

1. Register a single-tenant application in Microsoft Entra ID and record its application
   ID and tenant ID. Do **not** enable public-client flows or add a redirect URI for
   this client-credentials sample.
2. Add the application permissions appropriate to your scenario and grant tenant
   administrator consent:

| API | Application permission | When needed |
| --- | --- | --- |
| Microsoft Purview Information Protection Sync Service (also shown as Microsoft Information Protection Sync Service) | `UnifiedPolicy.Tenant.Read` | Download tenant labeling policy |
| Azure Rights Management Services | `Content.Writer` | Protect content owned by the application; see the offline-publishing caveat below |

Do not automatically grant `Content.SuperUser`, `Content.DelegatedReader`, or
`Content.DelegatedWriter`. Those serve different scenarios with broader access.
An application that processes existing protected content also needs the appropriate
rights to that content. Review the
[MIP permission reference](https://learn.microsoft.com/information-protection/develop/concept-api-permissions)
before extending this sample.

Application authentication obtains tenant policy, not one user's published-label
view. The engine uses `clientId@tenant` as a stable engine/cache identity. This is
not a user account and is not a principal to add to a user label-publishing policy.
This sample does not set `DelegatedUserEmail`.

### Known issue: offline publishing with service principals

The File SDK enables offline publishing by default. A known issue requires
service principals to have super-user privileges to retrieve the full RMS
templates used by offline publishing. With `Content.Writer` alone, applying a
protected label can fail with `TemplateNotFoundException` even when its template
exists.

Rather than automatically granting broader privileges, use online publishing by
disabling **offline** publishing. Add this property to the `MipConfiguration`
initializer in `MipHandler.CreateAsync`, before `MIP.CreateMipContext`:

```csharp
FeatureSettingsOverride = new Dictionary<FlightingFeature, bool>
{
    [FlightingFeature.FileSDKOfflinePublishing] = false
},
```

The same override is shown as a commented-out example in the source; the sample
leaves the SDK default unchanged until you explicitly enable the workaround.
Online publishing requires connectivity to the Rights Management service.
This flag is separate from `MipConfiguration.IsOfflineOnly`: the existing
constructor argument allowing network operations does not disable offline
publishing. See the [offline-publishing overview](https://learn.microsoft.com/information-protection/develop/concept-offline-publishing).

## Configure authentication

Configuration is read in increasing precedence:

1. `appsettings.json` beside the application, containing safe defaults and blank credentials.
2. Optional `appsettings.local.json` in the **current working directory**.
3. Environment variables with the `MIP_` prefix. Use `__` for nested settings.

Do not edit checked-in settings to contain real credentials. Local settings are
gitignored and excluded from build/publish copying. Environment variables are
convenient for development, but are not a secret vault.

### Certificate, recommended

Upload the certificate's **public certificate** to the Entra application. The
corresponding certificate and accessible private key must exist in
`Cert:\CurrentUser\My` for the Windows account running the application.
The sample does not generate, import, export, or upload certificates.

```powershell
$env:MIP_Ida__ClientId = '<application-guid>'
$env:MIP_Ida__Tenant = '<tenant-guid-or-verified-domain>'
$env:MIP_Ida__DoCertAuth = 'true'
$env:MIP_Ida__CertThumbprint = '<40-hex-character-thumbprint>'
```

The thumbprint is a SHA-1 lookup identifier, not the certificate's signing algorithm.
Whitespace is normalized. Missing certificates, missing private keys, and expired
or not-yet-valid certificates fail before SDK initialization.

### Client secret, development alternative

Use a secret value supplied by your approved secret-management mechanism, not the
secret's identifier. Avoid putting it in command-line arguments, scripts, or shell history.

```powershell
$env:MIP_Ida__DoCertAuth = 'false'
# Set MIP_Ida__ClientSecret through your local secret-management mechanism.
# ClientId and Tenant are still required.
```

Alternatively, an ignored `appsettings.local.json` can contain the same `Ida` and
`App` properties as `appsettings.json`. Protect its filesystem access and never
include it in shared archives.

### Cloud selection

`App:Cloud` defaults to `Commercial` and is explicitly assigned to
`FileEngineSettings.Cloud`. For a sovereign-cloud tenant, set the matching MIP
`Cloud` enum name, for example:

```powershell
$env:MIP_App__Cloud = 'US_GCC_High'
```

The SDK selects policy and protection endpoints for that cloud; the sample does
not maintain authority/resource hostname allowlists or use an engine identity
for cloud discovery. Authentication retains HTTPS/URI validation, configured-tenant
pinning, and MSAL's default authority validation. `Unknown`, `Custom`, and `Test`
are rejected; custom endpoint overrides are outside this sample's scope.

### Service consent and diagnostics

Before running live commands, review and explicitly accept that this application
contacts Microsoft Entra and Microsoft Information Protection services to authenticate,
download policy, and perform requested labeling/protection operations:

```powershell
$env:MIP_App__AcceptServiceConsent = 'true'
```

This is operator consent for the sample's service connection, **not** an Entra
permission grant or a replacement for admin consent.

Automatic SDK diagnostic telemetry and trace upload are disabled. Local SDK logging
defaults to `Warning`; opt-in `--audit` enables discovery auditing and successful
commit notification for that operation. This does not disable Microsoft service-side
security or operational logging. Labels, paths, tenant identifiers, and application
identifiers can appear in local logs or audit records. Do not publish caches or
logs without reviewing/redacting them.

| Setting | Default / behavior |
| --- | --- |
| `App:CachePath` | `%LOCALAPPDATA%\MipSdkSample\<client-id>\<tenant>`; SDK cache storage is encrypted |
| `App:LogLevel` | `Warning`; supported values: `Trace`, `Info`, `Warning`, `Error` |
| `App:AcceptServiceConsent` | `false`; required for live commands |
| `App:Name`, `App:Version` | Application identity metadata supplied to the SDK |

Use `MIP_App__CachePath` or `MIP_App__LogLevel` to override those settings.
Local logs are under the cache root, normally in `mip\logs`.
Retain only what your organization's retention policy permits. Stop all SDK
processes before removing a cache; native logging can hold files open until process exit.

## Build and run

Run these PowerShell commands from the repository root:

```powershell
dotnet restore .\MipSdkDotNetFileSPA.sln
dotnet build .\MipSdkDotNetFileSPA.sln --no-restore --configuration Release

dotnet run --project .\mip-sdk-dotnet-file-SPA --configuration Release -- --help
dotnet run --project .\mip-sdk-dotnet-file-SPA --configuration Release -- list
dotnet run --project .\mip-sdk-dotnet-file-SPA --configuration Release -- get --input 'C:\Test\input.docx'
dotnet run --project .\mip-sdk-dotnet-file-SPA --configuration Release -- set --input 'C:\Test\input.docx' --output 'C:\Test\labeled.docx' --label '<label-guid>'
```

With no arguments, the sample lists labels and prompts for a label ID and input/output
paths. A blank label or end-of-input exits normally. `--help` does not initialize
the SDK or require configuration. Add `--audit` to `get` or `set` only when wanted.

Choose an active **leaf label**, not a parent label, and an output file with the
appropriate extension for the input format. This sample does not convert file formats.
Use unprotected or administrator-defined protection labels; labels requiring
user-defined permissions need additional implementation.

File behavior:

- The input is never overwritten. Output must be a new path in an existing directory.
- The SDK evaluates every labeling request, even when the label ID already matches.
  Only an SDK no-op returns **unchanged** and creates no output; reapplying the
  same label can produce an updated file.
- A changed file is committed to a unique same-directory staging file, then moved
  without overwriting an existing destination, including a competing writer.
- Success requires reading the output back and matching its label ID. When the
  label requires Rights Management protection, read-back must also confirm that
  protection was applied by the label. A failed verification leaves the output
  for inspection and reports an error, without successful commit notification.
  Staging files are cleaned on ordinary failures; a crash can leave a `.mip-*` file.
- Successful commit notification uses the final output path, never the input path.

Exit codes: `0` success/unchanged, `2` invalid command or argument, `1` operational,
authentication, configuration, or SDK failure. MSAL errors print an error code
and correlation ID rather than token/server-response details. Other SDK errors
can contain tenant or file metadata; treat console output accordingly.

### Downgrade justification: code-only example

The CLI does not prompt for justification, set it automatically, or retry a
rejected downgrade. If tenant policy requires justification for the requested
lower-priority label, this SDK call throws
`Microsoft.InformationProtection.Exceptions.JustificationRequiredException`:

```csharp
// handler is an IFileHandler; lowerPriorityLabel is the requested Label.
handler.SetLabel(
    lowerPriorityLabel,
    new LabelingOptions { AssignmentMethod = AssignmentMethod.Standard },
    new ProtectionSettings());
```

The CLI reports the exception and returns exit code `1`; it does not commit output.
For an explicitly approved downgrade in your own integration, replace the
`LabelingOptions` passed to `handler.SetLabel` with a meaningful justification:

```csharp
handler.SetLabel(
    lowerPriorityLabel,
    new LabelingOptions
    {
        AssignmentMethod = AssignmentMethod.Standard,
        IsDowngradeJustified = true,
        JustificationMessage = "Approved downgrade after sensitive content was removed."
    },
    new ProtectionSettings());
```

The reason above is illustrative, not a default or an automatic exception fallback.
Only supply an accurate reason for an approved action. Justification does not
grant content rights or bypass authorization; ordinary commit and read-back
verification are still required after a successful `SetLabel`.

## Tests and test plan

The default suite needs no credentials and includes native Windows x64 initialization.
Integration cases are skipped unless explicitly enabled.

```powershell
# Credential-free unit tests and native SDK initialization
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --filter 'Category!=Integration'

# Unit tests without loading native SDK DLLs
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --filter 'Category!=Integration&Category!=Native'

# Native dependency smoke test only
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --filter 'Category=Native'

# Application coverage, using MTP rather than the VSTest --collect switch
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --filter 'Category!=Integration' --coverlet --coverlet-include '[MipSdkDotNetQuickstart]*' --coverlet-output-format cobertura --results-directory .\TestResults
```

Do not run coverage instrumentation alongside other test processes using the same
build output directory. Coverlet temporarily instruments the application assembly.

| Layer | Cases |
| --- | --- |
| Configuration | Missing/placeholder settings, tenant/client IDs, auth modes, certificate thumbprints, explicit cloud selection, logging, consent, JSON/local/environment precedence |
| Authentication | Authority/resource URI normalization, tenant pinning, `/.default` scopes, claims forwarding, MSAL client/token reuse using offline HTTP, certificate validity/private keys, disposal |
| SDK workflow | Explicit engine cloud without identity-based discovery, partial initialization and cleanup errors, disposal order, managed label snapshots, invalid/inactive/parent labels, input/output validation |
| File writes | SDK-driven no-op, same-ID protection repair, commit false/exception, staging cleanup, competing output writer, read-back absent/mismatch/missing protection, missing justification without retry, original input preserved, opt-in audit and output-path notification |
| CLI | Help without credentials, invalid/duplicate options, exit codes, no-label/EOF handling, interactive/command workflows, sanitized MSAL failures |
| Native/publish | Windows x64 DLL initialization in test output and the published directory, safe configuration packaging |
| Live tenant, opt-in | Policy enumeration; generated DOCX/PDF label, read-back, input preservation, and SDK-driven repeat-label outcome |
| Optional live protection | Protected generated DOCX when an approved protection label is explicitly supplied |

For live tests, set the authentication and consent environment variables above.
Use a test tenant and generated content only:

```powershell
$env:MIP_TEST_RUN = 'true'
# Optional: explicitly select an active, unprotected, file-scoped leaf label.
# Otherwise the test selects the first active unprotected leaf returned.
$env:MIP_TEST_LABEL_ID = '<unprotected-label-guid>'
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --filter 'Category=Integration' --timeout 3m
Remove-Item Env:\MIP_TEST_RUN
Remove-Item Env:\MIP_TEST_LABEL_ID
```

The optional protection case also requires `MIP_TEST_PROTECTED_LABEL_ID`, sufficient
content rights, and a label with administrator-defined protection suitable for the
test app. Files are generated in a unique temporary directory and removed afterward.
Live tests use the configured persistent SDK cache. They do not change tenant
settings, labels, permissions, or application credentials.

Run the same integration cases separately in certificate and secret modes to exercise
both live authentication paths. Permission-denied/expired-credential scenarios should
use dedicated test registrations, not changes to a working production application.
The suite does not claim to test all protection templates, cloud regions, downgrade
policies, conditional access configurations, or service-side audit delivery.

## Publish and automation

```powershell
dotnet publish .\mip-sdk-dotnet-file-SPA\MipSdkDotNetFileSPA.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\publish
pwsh -NoProfile -File .\scripts\Test-Publish.ps1 -PublishDirectory .\artifacts\publish
```

Keep the entire publish directory. Do not enable trimming, Native AOT, or single-file
bundling without separately validating MIP's native interop/dependency requirements.
Framework-dependent deployments require the .NET 10 x64 runtime.

Windows CI restores, builds with warnings as errors, runs credential-free tests
and enforces at least 80% application line coverage, audits direct/transitive NuGet
dependencies, and verifies published native DLL loading. CodeQL has a separate
scheduled/PR workflow. Dependabot checks
NuGet and GitHub Actions weekly. These workflows do **not** use tenant credentials.

For a local dependency advisory check:

```powershell
dotnet list .\MipSdkDotNetFileSPA.sln package --vulnerable --include-transitive
```

NuGet advisory checks do not independently analyze native libraries bundled inside
MIP. Review vendor release notes and your organization's scanning results before
deployment. Snyk Code scanning requires the organization's configured Snyk tooling;
it is not replaced by a successful dependency advisory check.

## Implementation notes and troubleshooting

`SampleSettings` validates configuration. `AuthDelegateImplementation` owns one
certificate and caches confidential clients per normalized authority. MSAL retains
an in-memory application token cache; the unavoidable synchronous SDK auth callback
is the only sync-over-async boundary. `MipHandler` owns engine, profile, and context,
disposes partial initialization safely, and returns managed label snapshots.
Reuse a session rather than rebuilding engines for each file; finish operations
before disposing it. Session operations/disposal are not designed to race.

| Symptom | Check |
| --- | --- |
| Native DLL load/bad image error | Windows x64 process, x64 VC++ runtime, intact `x64` native directory |
| Certificate not found/private key inaccessible | `CurrentUser\My` for the actual process account, validity and private-key ACLs |
| Authentication/authorization failure | Tenant and credential registration, expiry, API application permissions, admin consent, content rights; use the correlation ID |
| No usable labels | Matching `App:Cloud`, tenant labeling configuration, file scope, active leaf labels, tenant-policy permission; the engine ID is not a user to publish labels to |
| Output already exists | Choose a new name; the sample intentionally has no overwrite option |
| Protected-label failure | Template permissions, content rights, supported file format, and whether the label requires unsupported user-defined permissions |
| `TemplateNotFoundException` | Check the known service-principal offline-publishing issue above: template retrieval can require super-user privileges even with `Content.Writer`; disable offline publishing to use online publishing instead. Also verify template availability and access |

See the [MIP SDK documentation](https://learn.microsoft.com/information-protection/develop/),
[MSAL client-credentials guidance](https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/web-apps-apis/client-credential-flows),
[contributor guide](CONTRIBUTING.md), and [changelog](CHANGELOG.md).
The sample is licensed under the [MIT license](LICENSE.md).
