// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.InformationProtection;
using MipSdkDotNetQuickstart;

namespace MipSdkDotNetFileSPA.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "MipSdkSampleTests", Guid.NewGuid().ToString("N"));

    internal TemporaryDirectory() => Directory.CreateDirectory(Root);
    internal string PathFor(string name) => Path.Combine(Root, name);

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal static class TestSupport
{
    internal const string ClientId = "00000000-0000-0000-0000-000000000002";
    internal const string Tenant = "00000000-0000-0000-0000-000000000001";
    internal const string LabelId = "00000000-0000-0000-0000-000000000003";

    internal static Dictionary<string, string?> ConfigurationValues() => new()
    {
        ["Ida:ClientId"] = ClientId,
        ["Ida:Tenant"] = Tenant,
        ["Ida:DoCertAuth"] = "false",
        ["Ida:ClientSecret"] = "unit-test-credential",
        ["App:Name"] = "MIP SDK tests",
        ["App:Version"] = "1.0",
        ["App:AcceptServiceConsent"] = "true"
    };

    internal static SampleSettings Settings(Dictionary<string, string?>? values = null) =>
        SampleSettings.Load(new ConfigurationBuilder().AddInMemoryCollection(values ?? ConfigurationValues()).Build());

    internal static Label Label(string id = LabelId, bool active = true, params Label[] children) => new(id, ActionSource.Manual)
    {
        Name = "Test label",
        IsActive = active,
        Children = new ReadOnlyCollection<Label>(children)
    };

    internal static void WriteDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        Write("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        Write("word/document.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>Generated MIP SDK test fixture. No customer data.</w:t></w:r></w:p><w:sectPr/></w:body>
            </w:document>
            """);

        void Write(string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }

    internal static void WritePdf(string path)
    {
        const string content = "BT /F1 12 Tf 72 720 Td (Generated MIP SDK test fixture.) Tj ET\n";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream"
        ];
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        System.IO.File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
