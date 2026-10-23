// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Microsoft.Identity.Client;
using Microsoft.InformationProtection;
using Microsoft.InformationProtection.File;
using MipSdkDotNetQuickstart;
using Moq;

namespace MipSdkDotNetFileSPA.Tests;

public sealed class SampleApplicationTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task Help_SucceedsWithoutConfigurationOrSdk(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await SampleApplication.RunAsync([argument], TextReader.Null, output, error,
            () => throw new InvalidOperationException("must not initialize"));
        Assert.Equal(0, result);
        Assert.Contains("set --input", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    public static TheoryData<string[]> InvalidCommands => new()
    {
        new[] { "unknown" },
        new[] { "list", "--audit" },
        new[] { "list", "--input", "file" },
        new[] { "get" },
        new[] { "get", "--input" },
        new[] { "get", "--input", " " },
        new[] { "get", "--input", "file", "--input", "again" },
        new[] { "get", "--input", "file", "--audit", "--audit" },
        new[] { "set", "--input", "file", "--output", "output" },
        new[] { "--help", "list" }
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public async Task InvalidCommand_ReturnsUsageExitBeforeInitialization(string[] arguments)
    {
        using var error = new StringWriter();
        var result = await SampleApplication.RunAsync(arguments, TextReader.Null, TextWriter.Null, error,
            () => throw new InvalidOperationException("must not initialize"));
        Assert.Equal(2, result);
        Assert.Contains("ArgumentException", error.ToString());
    }

    [Fact]
    public void Parse_RecognizesSetWithAuditInAnyPosition()
    {
        var result = CommandOptions.Parse(["set", "--audit", "--label", TestSupport.LabelId, "--output", "new.docx", "--input", "old.docx"]);
        Assert.Equal(new CommandOptions("set", "old.docx", "new.docx", TestSupport.LabelId, true), result);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("interactive")]
    public async Task EmptyLabelsOrEndOfInput_ExitWithoutBlocking(string command)
    {
        var engine = new Mock<IFileEngine>();
        engine.SetupGet(value => value.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([]));
        using var output = new StringWriter();
        var result = await SampleApplication.RunAsync(command == "list" ? ["list"] : [], TextReader.Null, output,
            TextWriter.Null, () => Task.FromResult(new MipHandler(engine.Object)));
        Assert.Equal(0, result);
        Assert.Contains("No sensitivity labels", output.ToString());
        engine.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task AuthenticationFailure_DoesNotPrintServerPayloadOrCredential()
    {
        using var error = new StringWriter();
        var result = await SampleApplication.RunAsync(["list"], TextReader.Null, TextWriter.Null, error,
            () => throw new MsalServiceException("invalid_client", "sensitive server payload"));
        Assert.Equal(1, result);
        Assert.Contains("invalid_client", error.ToString());
        Assert.DoesNotContain("sensitive server payload", error.ToString());
    }

    [Fact]
    public async Task FileFailure_ReturnsOperationalExitCode()
    {
        using var error = new StringWriter();
        var result = await SampleApplication.RunAsync(["get", "--input", "missing"], TextReader.Null, TextWriter.Null, error,
            () => throw new FileNotFoundException("test missing file"));
        Assert.Equal(1, result);
        Assert.Contains("FileNotFoundException", error.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_ReportsLabelsOrUnlabeledFiles(bool labeled)
    {
        using var directory = new TemporaryDirectory();
        var input = directory.PathFor("input.txt");
        System.IO.File.WriteAllText(input, "fixture");
        var file = new Mock<IFileHandler>();
        file.SetupGet(value => value.Label).Returns(labeled
            ? new ContentLabel(TestSupport.Label(), DateTime.UtcNow, AssignmentMethod.Standard, false) : null!);
        var engine = new Mock<IFileEngine>();
        engine.Setup(value => value.CreateFileHandlerAsync(input, input, true, null, true)).ReturnsAsync(file.Object);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var result = await SampleApplication.RunAsync(["get", "--input", input, "--audit"], TextReader.Null, output, error,
            () => Task.FromResult(new MipHandler(engine.Object)));
        Assert.Equal(0, result);
        Assert.Contains(labeled ? TestSupport.LabelId : "no sensitivity label", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        file.Verify(value => value.Dispose(), Times.Once);
        engine.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SetOrInteractive_ReportsVerifiedOrUnchangedResults(bool interactive, bool modified)
    {
        using var directory = new TemporaryDirectory();
        var input = directory.PathFor("input.txt");
        var destination = directory.PathFor("output.txt");
        System.IO.File.WriteAllText(input, "fixture");
        var label = TestSupport.Label();
        var file = new Mock<IFileHandler>();
        file.Setup(value => value.IsModified()).Returns(modified);
        file.Setup(value => value.CommitAsync(It.IsAny<string>())).Returns((string path) =>
        {
            System.IO.File.WriteAllText(path, "labeled");
            return Task.FromResult(true);
        });
        var readback = new Mock<IFileHandler>();
        readback.SetupGet(value => value.Label).Returns(new ContentLabel(label, DateTime.UtcNow, AssignmentMethod.Standard, false));
        var engine = new Mock<IFileEngine>();
        engine.SetupGet(value => value.SensitivityLabels).Returns(new ReadOnlyCollection<Label>([label]));
        engine.Setup(value => value.GetLabelById(TestSupport.LabelId)).Returns(label);
        engine.Setup(value => value.CreateFileHandlerAsync(input, input, false, null, false)).ReturnsAsync(file.Object);
        engine.Setup(value => value.CreateFileHandlerAsync(destination, destination, false, null, false)).ReturnsAsync(readback.Object);
        using var reader = new StringReader($"{TestSupport.LabelId}\n{input}\n{destination}\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = interactive ? [] : ["set", "--input", input, "--output", destination, "--label", TestSupport.LabelId];
        var result = await SampleApplication.RunAsync(args, reader, output, error, () => Task.FromResult(new MipHandler(engine.Object)));
        Assert.Equal(0, result);
        Assert.Contains(modified ? "applied and verified" : "No output file", output.ToString());
        Assert.Equal(modified, System.IO.File.Exists(destination));
        Assert.Equal(string.Empty, error.ToString());
    }
}
