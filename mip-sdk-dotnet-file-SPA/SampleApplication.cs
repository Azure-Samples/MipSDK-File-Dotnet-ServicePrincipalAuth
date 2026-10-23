// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Identity.Client;

namespace MipSdkDotNetQuickstart;

internal sealed record CommandOptions(string Command, string? Input = null, string? Output = null, string? Label = null, bool Audit = false)
{
    internal static CommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new("interactive");
        }

        if (args is ["--help"] or ["-h"] or ["help"])
        {
            return new("help");
        }

        if (args[0] is not ("list" or "get" or "set"))
        {
            throw new ArgumentException("Use list, get, set, or --help.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var audit = false;
        for (var index = 1; index < args.Length; index++)
        {
            var key = args[index];
            if (key == "--audit" && !audit)
            {
                audit = true;
                continue;
            }

            if (key is not ("--input" or "--output" or "--label") ||
                index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++index]))
            {
                throw new ArgumentException($"Unknown, duplicate, or incomplete option: {key}.");
            }
        }

        var allowed = args[0] switch
        {
            "list" => Array.Empty<string>(),
            "get" => ["--input"],
            _ => new[] { "--input", "--output", "--label" }
        };
        if (values.Count != allowed.Length || allowed.Any(key => !values.ContainsKey(key)) ||
            values.Any(pair => string.IsNullOrWhiteSpace(pair.Value)) || (args[0] == "list" && audit))
        {
            throw new ArgumentException("get requires --input; set requires --input, --output, and --label; list takes no options.");
        }

        return new(args[0], values.GetValueOrDefault("--input"), values.GetValueOrDefault("--output"),
            values.GetValueOrDefault("--label"), audit);
    }
}

internal static class SampleApplication
{
    internal const string Help = """
        MIP SDK File API sample (Windows x64, application authentication)
          list
          get --input <path> [--audit]
          set --input <path> --output <new-path> --label <guid> [--audit]
          --help
        With no arguments, prompts for a label and file paths. No files are overwritten.
        Credentials are read from appsettings.json, optional appsettings.local.json in
        the current directory, then MIP_ environment variables. See README.md.
        """;

    internal static async Task<int> RunAsync(
        string[] args, TextReader input, TextWriter output, TextWriter error, Func<Task<MipHandler>> createHandler)
    {
        try
        {
            var command = CommandOptions.Parse(args);
            if (command.Command == "help")
            {
                await output.WriteLineAsync(Help);
                return 0;
            }

            using var mip = await createHandler();
            if (command.Command is "list" or "interactive")
            {
                var labels = mip.ListLabels();
                foreach (var label in labels)
                {
                    WriteLabel(output, label, string.Empty);
                }

                if (labels.Count == 0)
                {
                    await output.WriteLineAsync("No sensitivity labels were returned for this tenant.");
                }

                if (command.Command == "list")
                {
                    return 0;
                }

                output.Write("Label ID (blank to exit): ");
                var labelId = (await input.ReadLineAsync())?.Trim();
                if (string.IsNullOrEmpty(labelId))
                {
                    return 0;
                }

                output.Write("Input file path: ");
                var inputPath = await input.ReadLineAsync();
                output.Write("New output file path: ");
                var outputPath = await input.ReadLineAsync();
                command = new("set", inputPath ?? string.Empty, outputPath ?? string.Empty, labelId);
            }

            if (command.Command == "get")
            {
                var label = await mip.GetLabelAsync(command.Input!, command.Audit);
                await output.WriteLineAsync(label is null
                    ? "The file has no sensitivity label."
                    : $"Label: {label.Name}\nID: {label.Id}\nProtected by label: {label.IsProtected}");
            }
            else
            {
                var result = await mip.SetLabelAsync(command.Input!, command.Output!, command.Label!, command.Audit);
                await output.WriteLineAsync(result == LabelResult.Applied
                    ? $"Label applied and verified. Output: {Path.GetFullPath(command.Output!.Trim().Trim('"'))}"
                    : "The file is unchanged. No output file was created.");
            }

            return 0;
        }
        catch (MsalException exception)
        {
            await error.WriteLineAsync($"Authentication failed ({exception.ErrorCode}). Correlation ID: {exception.CorrelationId}");
            return 1;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"{exception.GetType().Name}: {exception.Message}");
            return exception is ArgumentException ? 2 : 1;
        }
    }

    private static void WriteLabel(TextWriter output, LabelInfo label, string indent)
    {
        output.WriteLine($"{indent}{label.Name} | {label.Id} | Active: {label.IsActive} | Protection: {label.IsProtected}");
        foreach (var child in label.Children)
        {
            WriteLabel(output, child, indent + "  ");
        }
    }
}
