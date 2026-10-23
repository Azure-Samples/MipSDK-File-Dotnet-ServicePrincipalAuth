// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Exceptions;
using Microsoft.InformationProtection.File;
using MipSdkDotNetQuickstart;
using Moq;

namespace MipSdkDotNetFileSPA.Tests;

public sealed class MipHandlerTests
{
    [Theory]
    [InlineData(Cloud.Commercial)]
    [InlineData(Cloud.US_GCC)]
    [InlineData(Cloud.US_GCC_High)]
    [InlineData(Cloud.US_DoD)]
    [InlineData(Cloud.China_01)]
    public void EngineSettings_UseExplicitCloudWithoutIdentityDiscovery(Cloud cloud)
    {
        var values = TestSupport.ConfigurationValues();
        values["App:Cloud"] = cloud.ToString();
        var auth = new Mock<IAuthDelegate>();
        var settings = MipHandler.CreateEngineSettings(TestSupport.Settings(values), auth.Object);
        Assert.Equal(cloud, settings.Cloud);
        Assert.Null(settings.Identity);
        Assert.Equal($"{TestSupport.ClientId}@{TestSupport.Tenant}", settings.EngineId);
    }

    [Fact]
    public async Task InitializationFailure_ReleasesPartialResourcesAndPreservesCause()
    {
        var disposed = new List<string>();
        var profile = new Mock<IFileProfile>();
        profile.Setup(value => value.Dispose()).Callback(() => disposed.Add("profile"));
        var cause = new IOException("engine initialization failed");
        var result = await Assert.ThrowsAsync<IOException>(() => MipHandler.CreateAsync(
            () => Task.FromResult(profile.Object),
            _ => Task.FromException<IFileEngine>(cause),
            () => disposed.Add("context")));
        Assert.Same(cause, result);
        Assert.Equal(["profile", "context"], disposed);
    }

    [Fact]
    public async Task ProfileFailure_StillReleasesContextAndPreservesCleanupFailure()
    {
        var cause = new IOException("profile initialization failed");
        var cleanup = new InvalidOperationException("cleanup failed");
        var result = await Assert.ThrowsAsync<IOException>(() => MipHandler.CreateAsync(
            () => Task.FromException<IFileProfile>(cause),
            _ => throw new NotSupportedException(),
            () => throw cleanup));
        Assert.Same(cause, result);
        Assert.Same(cleanup, result.Data["CleanupError"]);
    }

    [Fact]
    public async Task Dispose_IsOrderedIdempotentAndRejectsLaterOperations()
    {
        var disposed = new List<string>();
        var engine = new Mock<IFileEngine>();
        var profile = new Mock<IFileProfile>();
        engine.Setup(value => value.Dispose()).Callback(() => disposed.Add("engine"));
        profile.Setup(value => value.Dispose()).Callback(() => disposed.Add("profile"));
        var handler = new MipHandler(engine.Object, profile.Object, () => disposed.Add("context"));
        handler.Dispose();
        handler.Dispose();
        Assert.Equal(["engine", "profile", "context"], disposed);
        Assert.Throws<ObjectDisposedException>(() => handler.ListLabels());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.GetLabelAsync("unused"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.SetLabelAsync("unused", "unused", TestSupport.LabelId));
    }

    [Fact]
    public void Dispose_ContinuesWhenEngineDisposalFails()
    {
        var engine = new Mock<IFileEngine>();
        var profile = new Mock<IFileProfile>();
        var released = false;
        engine.Setup(value => value.Dispose()).Throws<IOException>();
        var handler = new MipHandler(engine.Object, profile.Object, () => released = true);
        Assert.Throws<IOException>(() => handler.Dispose());
        profile.Verify(value => value.Dispose(), Times.Once);
        Assert.True(released);
        handler.Dispose();
    }

    [Fact]
    public void ListLabels_ReturnsRecursiveManagedSnapshots()
    {
        var child = TestSupport.Label();
        var parent = TestSupport.Label(Guid.NewGuid().ToString(), true, child);
        var engine = new Mock<IFileEngine>();
        engine.SetupGet(value => value.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([parent]));
        using var handler = new MipHandler(engine.Object);
        var snapshot = Assert.Single(handler.ListLabels());
        child.Name = "changed after snapshot";
        Assert.Equal(TestSupport.LabelId, Assert.Single(snapshot.Children).Id);
        Assert.Equal("Test label", snapshot.Children[0].Name);
    }

    [Theory]
    [InlineData("template")]
    [InlineData("adhoc")]
    [InlineData("do-not-forward")]
    [InlineData("encrypt-only")]
    public void ListLabels_RecognizesProtectionModes(string kind)
    {
        var label = TestSupport.Label();
        label.HasRightsManagementPolicy = kind == "template";
        label.HasAdhocProtection = kind == "adhoc";
        label.HasDoNotForwardProtection = kind == "do-not-forward";
        label.HasEncryptOnlyProtection = kind == "encrypt-only";
        var engine = new Mock<IFileEngine>();
        engine.SetupGet(value => value.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([label]));
        using var handler = new MipHandler(engine.Object);
        Assert.True(Assert.Single(handler.ListLabels()).IsProtected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task GetLabel_MapsContentAndDisposesNativeHandler(bool labeled, bool audit)
    {
        using var files = new TemporaryDirectory();
        var path = files.PathFor("input.txt");
        System.IO.File.WriteAllText(path, "fixture");
        var file = new Mock<IFileHandler>();
        file.SetupGet(value => value.Label).Returns(labeled ? Content(TestSupport.Label()) : null!);
        var engine = EngineWith(file);
        using var handler = new MipHandler(engine.Object);
        var result = await handler.GetLabelAsync($"\"{path}\"", audit);
        if (labeled) { Assert.Equal(TestSupport.LabelId, result!.Id); }
        else { Assert.Null(result); }
        file.Verify(value => value.Dispose(), Times.Once);
        VerifyCreate(engine, path, audit, getAudit: audit);
    }

    [Theory]
    [InlineData("invalid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("parent")]
    public async Task SetLabel_RejectsInvalidOrUnavailableLabelsBeforeCreatingHandler(string kind)
    {
        using var files = new TemporaryDirectory();
        var input = files.PathFor("input.txt");
        System.IO.File.WriteAllText(input, "fixture");
        var engine = new Mock<IFileEngine>(MockBehavior.Strict);
        engine.Setup(value => value.Dispose());
        if (kind is "unknown" or "inactive" or "parent")
        {
            engine.Setup(value => value.GetLabelById(TestSupport.LabelId)).Returns(
                kind == "unknown" ? null! : TestSupport.Label(TestSupport.LabelId, kind != "inactive",
                    kind == "parent" ? [TestSupport.Label()] : []));
        }
        using var handler = new MipHandler(engine.Object);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.SetLabelAsync(input, files.PathFor("output.txt"),
                kind is "unknown" or "inactive" or "parent" ? TestSupport.LabelId : kind));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetLabel_UnchangedDoesNotCommitAuditOrCreateOutput(bool alreadyLabeled)
    {
        using var files = new TemporaryDirectory();
        var input = files.PathFor("input.txt");
        var output = files.PathFor("output.txt");
        System.IO.File.WriteAllText(input, "fixture");
        var file = new Mock<IFileHandler>();
        file.SetupGet(value => value.Label).Returns(alreadyLabeled ? Content(TestSupport.Label()) : null!);
        file.Setup(value => value.IsModified()).Returns(false);
        var engine = EngineWith(file);
        using var handler = new MipHandler(engine.Object);
        Assert.Equal(LabelResult.Unchanged, await handler.SetLabelAsync(input, output, TestSupport.LabelId, audit: true));
        Assert.False(System.IO.File.Exists(output));
        file.Verify(value => value.SetLabel(It.IsAny<Label>(), It.IsAny<LabelingOptions>(),
            It.IsAny<ProtectionSettings>()), Times.Once);
        file.Verify(value => value.IsModified(), Times.Once);
        file.Verify(value => value.CommitAsync(It.IsAny<string>()), Times.Never);
        file.Verify(value => value.NotifyCommitSuccessful(It.IsAny<string>()), Times.Never);
        file.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData("success", false)]
    [InlineData("success", true)]
    [InlineData("same-label-repair", true)]
    [InlineData("protected-success", true)]
    [InlineData("commit-false", true)]
    [InlineData("commit-throw", true)]
    [InlineData("readback-mismatch", true)]
    [InlineData("readback-empty", true)]
    [InlineData("readback-unprotected", true)]
    [InlineData("output-race", true)]
    public async Task SetLabel_StagesVerifiesAuditsAndCleansUp(string outcome, bool audit)
    {
        using var files = new TemporaryDirectory();
        var input = files.PathFor("input.docx");
        var output = files.PathFor("output.docx");
        System.IO.File.WriteAllText(input, "original");
        var label = TestSupport.Label();
        label.HasRightsManagementPolicy = outcome is "same-label-repair" or "protected-success" or "readback-unprotected";
        var succeeded = outcome is "success" or "same-label-repair" or "protected-success";
        var file = new Mock<IFileHandler>();
        file.SetupGet(value => value.Label).Returns(outcome == "same-label-repair" ? Content(label) : null!);
        var readback = new Mock<IFileHandler>();
        readback.SetupGet(value => value.Label).Returns(outcome == "readback-empty" ? null! :
            Content(TestSupport.Label(outcome == "readback-mismatch" ? Guid.NewGuid().ToString() : TestSupport.LabelId),
                isProtected: outcome is "same-label-repair" or "protected-success"));
        file.Setup(value => value.IsModified()).Returns(true);
        string? staging = null;
        file.Setup(value => value.CommitAsync(It.IsAny<string>())).Returns((string path) =>
        {
            staging = path;
            System.IO.File.WriteAllText(path, "labeled");
            if (outcome == "output-race") { System.IO.File.WriteAllText(output, "racing writer"); }
            return outcome == "commit-throw"
                ? Task.FromException<bool>(new IOException("commit failed"))
                : Task.FromResult(outcome != "commit-false");
        });
        var engine = EngineWith(file, readback);
        engine.Setup(value => value.GetLabelById(TestSupport.LabelId)).Returns(label);
        using var handler = new MipHandler(engine.Object);
        if (succeeded)
        {
            Assert.Equal(LabelResult.Applied, await handler.SetLabelAsync(input, output, TestSupport.LabelId, audit));
            Assert.Equal("labeled", System.IO.File.ReadAllText(output));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<IOException>(() =>
                handler.SetLabelAsync(input, output, TestSupport.LabelId, audit));
            if (outcome == "readback-unprotected") { Assert.Contains("protection", exception.Message); }
            if (outcome == "output-race") { Assert.Equal("racing writer", System.IO.File.ReadAllText(output)); }
            if (outcome.StartsWith("readback", StringComparison.Ordinal)) { Assert.True(System.IO.File.Exists(output)); }
        }
        Assert.Equal("original", System.IO.File.ReadAllText(input));
        Assert.NotNull(staging);
        Assert.Equal(".docx", Path.GetExtension(staging));
        Assert.False(System.IO.File.Exists(staging));
        Assert.Empty(Directory.GetFiles(files.Root, ".mip-*"));
        file.Verify(value => value.SetLabel(It.Is<Label>(value => value.Id == TestSupport.LabelId),
            It.Is<LabelingOptions>(options => options.AssignmentMethod == AssignmentMethod.Standard),
            It.IsAny<ProtectionSettings>()), Times.Once);
        file.Verify(value => value.NotifyCommitSuccessful(output), succeeded && audit ? Times.Once : Times.Never);
        file.Verify(value => value.Dispose(), Times.Once);
        VerifyCreate(engine, input, audit);
        if (succeeded || outcome.StartsWith("readback", StringComparison.Ordinal))
        {
            VerifyCreate(engine, output, audit: false);
            readback.Verify(value => value.Dispose(), Times.Once);
        }
    }

    [Fact]
    public async Task SetLabel_MissingJustificationPropagatesWithoutRetryOrCommit()
    {
        using var files = new TemporaryDirectory();
        var input = files.PathFor("input.docx");
        var output = files.PathFor("output.docx");
        System.IO.File.WriteAllText(input, "original");
        var file = new Mock<IFileHandler>();
        var cause = new JustificationRequiredException("Policy requires justification for this downgrade.");
        file.Setup(value => value.SetLabel(It.IsAny<Label>(), It.IsAny<LabelingOptions>(),
            It.IsAny<ProtectionSettings>())).Throws(cause);
        var engine = EngineWith(file);
        using var handler = new MipHandler(engine.Object);

        var exception = await Assert.ThrowsAsync<JustificationRequiredException>(() =>
            handler.SetLabelAsync(input, output, TestSupport.LabelId, audit: true));

        Assert.Same(cause, exception);
        Assert.Equal("original", System.IO.File.ReadAllText(input));
        Assert.False(System.IO.File.Exists(output));
        Assert.Empty(Directory.GetFiles(files.Root, ".mip-*"));
        file.Verify(value => value.SetLabel(It.IsAny<Label>(),
            It.Is<LabelingOptions>(options => !options.IsDowngradeJustified &&
                string.IsNullOrEmpty(options.JustificationMessage)),
            It.IsAny<ProtectionSettings>()), Times.Once);
        file.Verify(value => value.IsModified(), Times.Never);
        file.Verify(value => value.CommitAsync(It.IsAny<string>()), Times.Never);
        file.Verify(value => value.NotifyCommitSuccessful(It.IsAny<string>()), Times.Never);
        file.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public void Paths_RejectMissingSameExistingAndInvalidDestinations()
    {
        using var files = new TemporaryDirectory();
        var input = files.PathFor("input.txt");
        var existing = files.PathFor("existing.txt");
        System.IO.File.WriteAllText(input, "fixture");
        System.IO.File.WriteAllText(existing, "keep");
        Assert.Throws<ArgumentException>(() => MipHandler.ValidateInputPath(" "));
        Assert.Throws<FileNotFoundException>(() => MipHandler.ValidateInputPath(files.PathFor("absent.txt")));
        Assert.Throws<ArgumentException>(() => MipHandler.ValidateOutputPath(input, " "));
        Assert.Throws<ArgumentException>(() => MipHandler.ValidateOutputPath(input, input.ToUpperInvariant()));
        Assert.Throws<IOException>(() => MipHandler.ValidateOutputPath(input, existing));
        Assert.Throws<IOException>(() => MipHandler.ValidateOutputPath(input, files.Root));
        Assert.Throws<DirectoryNotFoundException>(() => MipHandler.ValidateOutputPath(input, files.PathFor("absent\\output.txt")));
        Assert.Equal(input, MipHandler.ValidateInputPath($" \"{input}\" "));
        Assert.Equal("keep", System.IO.File.ReadAllText(existing));
    }

    private static ContentLabel Content(Label label, bool isProtected = false) =>
        new(label, DateTime.UtcNow, AssignmentMethod.Standard, isProtected);

    private static Mock<IFileEngine> EngineWith(params Mock<IFileHandler>[] handlers)
    {
        var engine = new Mock<IFileEngine>();
        engine.Setup(value => value.GetLabelById(TestSupport.LabelId)).Returns(TestSupport.Label());
        var queue = new Queue<Mock<IFileHandler>>(handlers);
        engine.Setup(value => value.CreateFileHandlerAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<FileExecutionState>(), It.IsAny<bool>()))
            .Returns(() => Task.FromResult(queue.Dequeue().Object));
        return engine;
    }

    private static void VerifyCreate(Mock<IFileEngine> engine, string path, bool audit, bool getAudit = false) =>
        engine.Verify(value => value.CreateFileHandlerAsync(path, path, audit, null, getAudit), Times.Once);
}
