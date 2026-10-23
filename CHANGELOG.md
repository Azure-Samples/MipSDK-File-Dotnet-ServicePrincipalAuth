# Changelog

## Unreleased

### Changed

- Modernized the .NET Framework sample to stable .NET 10, Windows x64.
- Updated MIP File SDK to 1.18.148, MSAL.NET to 4.91.1, and Configuration packages
  to 10.0.13. Removed preview and unnecessary direct package references.
- Added validated JSON/local/environment configuration, certificate-first defaults,
  configurable cache/logging, and explicit service consent.
- Reused MSAL clients and application token caches; certificate ownership and
  validity/private-key checks are explicit. Client credentials require no redirect URI.
- Set `FileEngineSettings.Cloud` explicitly through `App:Cloud` (default Commercial)
  instead of identity-based discovery or application-maintained endpoint allowlists.
  Retained HTTPS, URI-component, tenant, and MSAL authority validation.
- Converted file operations and initialization to async APIs with ordered cleanup
  on success and partial initialization failure.
- Replaced the interactive-only workflow with `list`, `get`, `set`, and `--help`,
  retaining no-argument interactive mode and adding deterministic exit codes.
- Made discovery/commit audit notifications opt-in and use the final output path.
- Added staged, no-overwrite file writes, SDK-driven no-op detection even for
  matching label IDs, and label/protection read-back verification. Input files
  are preserved; failed verification retains output without success notification.

### Added

- Credential-free configuration/authentication/workflow/CLI regression tests,
  Windows native smoke coverage, and explicitly enabled generated DOCX/PDF
  integration cases.
- Microsoft.Testing.Platform, xUnit v3, and application coverage reporting.
- Windows build/test/publish/advisory CI, CodeQL, and weekly Dependabot updates.
- Accurate app-only permission, certificate, diagnostics/privacy, testing,
  deployment, and contributor documentation.
- A code-only downgrade-justification example, with no prompts, automatic
  justification, or retry when the SDK requires a reason.
- Comments and troubleshooting guidance for the service-principal offline-publishing
  super-user template-fetch issue, including the optional online-publishing override
  without changing SDK defaults or granting additional permissions.

### Migration notes

- .NET Framework, x86, AnyCPU, `App.config`, and `Ida:RedirectUri` are no longer used.
- Set credentials through `MIP_` environment variables or ignored local JSON.
  Live operations require `App:AcceptServiceConsent=true`.
- Existing destinations are never overwritten; unchanged operations create no output.
- Tests use `dotnet test --solution ...` with the MTP runner, not VSTest-only
  coverage switches.
