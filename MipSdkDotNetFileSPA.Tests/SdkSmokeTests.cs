// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using MipSdkDotNetQuickstart;

namespace MipSdkDotNetFileSPA.Tests;

[CollectionDefinition("Native SDK", DisableParallelization = true)]
public sealed class NativeSdkCollection;

[Collection("Native SDK")]
public sealed class SdkSmokeTests
{
    [Fact]
    [Trait("Category", "Native")]
    public void NativeSdk_LoadsOnWindowsX64WithoutCredentials()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.True(Environment.Is64BitProcess);
        MipHandler.InitializeSdk();
    }

    public static bool LiveEnabled => Environment.GetEnvironmentVariable("MIP_TEST_RUN") == "true";
    public static bool ProtectionEnabled => LiveEnabled &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MIP_TEST_PROTECTED_LABEL_ID"));

    [Theory(Skip = "Set MIP_TEST_RUN=true and configure a test tenant to enable live tests.", SkipUnless = nameof(LiveEnabled))]
    [InlineData(".docx")]
    [InlineData(".pdf")]
    [Trait("Category", "Integration")]
    public async Task LiveTenant_ListsLabelsAndRoundTripsGeneratedFile(string extension)
    {
        await RoundTripAsync(extension, protectedLabel: false);
    }

    [Fact(Skip = "Set MIP_TEST_RUN=true and MIP_TEST_PROTECTED_LABEL_ID to enable protection tests.", SkipUnless = nameof(ProtectionEnabled))]
    [Trait("Category", "Integration")]
    public async Task LiveTenant_RoundTripsProtectedDocx()
    {
        await RoundTripAsync(".docx", protectedLabel: true);
    }

    private static async Task RoundTripAsync(string extension, bool protectedLabel)
    {
        using var directory = new TemporaryDirectory();
        var configuration = SampleSettings.LoadConfiguration();
        using var lifetime = configuration as IDisposable;
        using var mip = await MipHandler.CreateAsync(SampleSettings.Load(configuration));
        var labels = Flatten(mip.ListLabels()).ToArray();
        Assert.NotEmpty(labels);
        var requestedId = Environment.GetEnvironmentVariable(protectedLabel ? "MIP_TEST_PROTECTED_LABEL_ID" : "MIP_TEST_LABEL_ID");
        var label = string.IsNullOrWhiteSpace(requestedId)
            ? labels.FirstOrDefault(label => label.IsActive && !label.IsProtected && label.Children.Count == 0)
            : labels.FirstOrDefault(label => string.Equals(label.Id, requestedId, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(label);
        Assert.True(label.IsActive && label.Children.Count == 0);
        Assert.Equal(protectedLabel, label.IsProtected);
        var input = directory.PathFor($"generated{extension}");
        var output = directory.PathFor($"labeled{extension}");
        if (extension == ".docx") { TestSupport.WriteDocx(input); }
        else { TestSupport.WritePdf(input); }
        var original = await System.IO.File.ReadAllBytesAsync(input);
        Assert.Null(await mip.GetLabelAsync(input));
        Assert.Equal(LabelResult.Applied, await mip.SetLabelAsync(input, output, label.Id));
        Assert.Equal(original, await System.IO.File.ReadAllBytesAsync(input));
        var actual = await mip.GetLabelAsync(output);
        Assert.NotNull(actual);
        Assert.Equal(label.Id, actual.Id, ignoreCase: true);
        Assert.Equal(protectedLabel, actual.IsProtected);
        var labeledInput = await System.IO.File.ReadAllBytesAsync(output);
        var repeatOutput = directory.PathFor($"reapplied{extension}");
        var repeatResult = await mip.SetLabelAsync(output, repeatOutput, label.Id);
        Assert.Equal(labeledInput, await System.IO.File.ReadAllBytesAsync(output));
        if (repeatResult == LabelResult.Unchanged)
        {
            Assert.False(System.IO.File.Exists(repeatOutput));
        }
        else
        {
            Assert.Equal(LabelResult.Applied, repeatResult);
            Assert.True(System.IO.File.Exists(repeatOutput));
            var reapplied = await mip.GetLabelAsync(repeatOutput);
            Assert.NotNull(reapplied);
            Assert.Equal(label.Id, reapplied.Id, ignoreCase: true);
            Assert.Equal(protectedLabel, reapplied.IsProtected);
        }
    }

    private static IEnumerable<LabelInfo> Flatten(IEnumerable<LabelInfo> labels) =>
        labels.SelectMany(label => new[] { label }.Concat(Flatten(label.Children)));
}
