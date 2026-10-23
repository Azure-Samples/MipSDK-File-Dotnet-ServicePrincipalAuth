# Contributing

Contributions to the MIP SDK File API service-principal sample are welcome.
Keep changes focused, preserve its educational purpose, and discuss substantial
new authentication or document-processing features in an issue first.

## Development

Use Windows x64, a stable .NET 10 SDK, and the x64 Visual C++ runtime.
From the repository root:

```powershell
dotnet restore .\MipSdkDotNetFileSPA.sln
dotnet build .\MipSdkDotNetFileSPA.sln --configuration Release --no-restore
dotnet test --solution .\MipSdkDotNetFileSPA.sln --configuration Release --no-build --filter 'Category!=Integration'
```

Use the Microsoft.Testing.Platform commands in [README.md](README.md) for
coverage, native/publish checks, and explicitly enabled tenant tests.
CI must remain credential-free. Add regression tests for changed behavior,
especially authentication challenges, native lifetime, output safety, and CLI
exit codes. Do not replace certificate/secret validation with permissive test hooks.

Keep authentication material out of source, fixtures, logs, and pull requests.
Use environment variables or ignored local configuration for live testing.
Generate test documents rather than committing customer content. The live tests
must not modify tenant settings or process existing user documents.

Use async SDK operations outside the synchronous SDK auth callback, reuse MSAL
clients, dispose owned resources, and preserve the no-overwrite/read-back guarantees.
Update README and CHANGELOG when behavior or prerequisites change. Verify both
build and publish layouts when upgrading MIP, and review native dependency
advisories in addition to NuGet advisories.

## Issues and pull requests

Search existing issues and pull requests before opening another. Include the
SDK/package version, Windows/.NET version and architecture, reproduction steps,
and the actual versus expected result. For authentication failures, include a
correlation ID if appropriate, never tokens, secret values, private keys, or
unreviewed tenant logs.

Submit a focused pull request with a description of the behavior change and
the checks performed. Do not include local caches, configuration, build output,
or integration-test identities. Keep unrelated local changes out of the PR.

Most contributions require a Contributor License Agreement confirming that you
have the right to grant the contribution. The CLA bot provides instructions
when a pull request is opened. See [Microsoft CLA](https://cla.microsoft.com).

This project follows the
[Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
See the [FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact
[opencode@microsoft.com](mailto:opencode@microsoft.com) with questions.
For sensitive security reports, use the
[Microsoft Security Response Center](https://www.microsoft.com/msrc) rather than
disclosing exploitable details in a public issue.
