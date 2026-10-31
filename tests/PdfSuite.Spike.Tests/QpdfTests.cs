using System.Text;
using PdfSuite.FixtureGenerator;
using PdfSuite.Pdf.Abstractions;
using PdfSuite.Pdf.Pdfium;
using PdfSuite.Pdf.Qpdf;

namespace PdfSuite.Spike.Tests;

/// <summary>Spike S4: qpdf through P/Invoke. Results are checked with PDFium, an independent reader.</summary>
public sealed class QpdfTests
{
    private const string UserPassword = "user-pass";
    private const string OwnerPassword = "owner-pass";
    private readonly PdfiumEngine _pdfium = new();

    [Fact]
    public void Encrypt_Aes256_NeedsThePasswordInAnIndependentReader()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), UserPassword, OwnerPassword, new PdfPermissions());

        Assert.True(QpdfStructure.IsEncrypted(encrypted));
        var refused = Assert.Throws<PdfOpenException>(() => _pdfium.Open(encrypted));
        Assert.Equal(PdfOpenError.Password, refused.Error);
        Assert.Equal(PdfOpenError.Password, Assert.Throws<PdfOpenException>(() => _pdfium.Open(encrypted, "wrong")).Error);

        foreach (string password in new[] { UserPassword, OwnerPassword })
        {
            using IPdfDocument document = _pdfium.Open(encrypted, password);
            Assert.Equal(3, document.PageCount);
            Assert.Contains("Simple text page 1", document.GetPageText(0));
        }

        // AES-256 is the PDF 2.0 standard security handler, version 5 revision 6. The encryption
        // dictionary is never inside an encrypted or object stream, so it is visible in the bytes.
        string bytes = Encoding.Latin1.GetString(encrypted);
        Assert.Matches(@"/V\s*5", bytes);
        Assert.Matches(@"/R\s*6", bytes);
    }

    [Fact]
    public void Decrypt_WithThePassword_OpensWithoutOne_AndWithTheWrongPasswordFails()
    {
        byte[] encrypted = QpdfStructure.Encrypt(Fixtures.SimpleText(), UserPassword, OwnerPassword, new PdfPermissions());

        byte[] decrypted = QpdfStructure.Decrypt(encrypted, UserPassword);

        Assert.False(QpdfStructure.IsEncrypted(decrypted));
        using (IPdfDocument document = _pdfium.Open(decrypted))
        {
            Assert.Contains("Simple text page 3", document.GetPageText(2));
        }

        var error = Assert.Throws<QpdfException>(() => QpdfStructure.Decrypt(encrypted, "wrong"));
        Assert.Equal(4, error.Code);
    }

    [Fact]
    public void Metadata_RoundTrips_IncludingNonAsciiText_AndSurvivesEncryption()
    {
        const string title = "Résumé – naïve test ✓";
        byte[] updated = QpdfStructure.SetMetadata(Fixtures.SimpleText(), new Dictionary<string, string?>
        {
            ["/Title"] = title,
            ["/Author"] = "PDF Suite spike",
        });

        IReadOnlyDictionary<string, string> read = QpdfStructure.GetMetadata(updated, ["/Title", "/Author", "/Subject"]);
        Assert.Equal(title, read["/Title"]);
        Assert.Equal("PDF Suite spike", read["/Author"]);
        Assert.False(read.ContainsKey("/Subject"));

        using (var document = (PdfiumDocument)_pdfium.Open(updated))
        {
            Assert.Equal(title, document.GetMetadata("Title"));
        }

        byte[] encrypted = QpdfStructure.Encrypt(updated, UserPassword, OwnerPassword, new PdfPermissions());
        Assert.Equal(title, QpdfStructure.GetMetadata(encrypted, ["/Title"], UserPassword)["/Title"]);
        using (var document = (PdfiumDocument)_pdfium.Open(encrypted, UserPassword))
        {
            Assert.Equal(title, document.GetMetadata("Title"));
        }
    }

    [Fact]
    public void Metadata_NullValue_RemovesTheKey()
    {
        byte[] withTitle = QpdfStructure.SetMetadata(Fixtures.SimpleText(), new Dictionary<string, string?> { ["/Title"] = "Draft" });

        byte[] without = QpdfStructure.SetMetadata(withTitle, new Dictionary<string, string?> { ["/Title"] = null });

        Assert.Empty(QpdfStructure.GetMetadata(without, ["/Title"]));
    }

    [Fact]
    public void Rewrite_DropsEarlierRevisionsAndTheirContent()
    {
        byte[] incremental = Fixtures.CanaryIncremental();
        int firstEof = incremental.AsSpan().IndexOf("%%EOF"u8) + 5;

        // Negative control: the same search does find the canary in the first revision on its own,
        // so its absence after the rewrite means something.
        Assert.True(PdfText.Contains(QpdfStructure.Rewrite(incremental.AsSpan(0, firstEof), inspectable: true), "CANARY-06"));

        byte[] rewritten = QpdfStructure.Rewrite(incremental, inspectable: true);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(rewritten), "%%EOF"));
        Assert.False(PdfText.Contains(rewritten, "CANARY-06"), "The earlier revision's canary survived the rewrite.");
        Assert.True(PdfText.Contains(rewritten, "REVISED"));
        using IPdfDocument document = _pdfium.Open(rewritten);
        Assert.Contains("REVISED", document.GetPageText(0));
    }

    [Fact]
    public void PdfText_FindsTextInTheFormsPdfsUse()
    {
        Assert.True(PdfText.Contains("BT [<43414E4152592D3031>] TJ ET"u8, "CANARY-01"));
        Assert.True(PdfText.Contains("BT [<43414e4152592d3031>] TJ ET"u8, "CANARY-01"));
        Assert.True(PdfText.Contains("BT (CANARY-01) Tj ET"u8, "CANARY-01"));
        Assert.True(PdfText.Contains("/Title <FEFF00430041004E004100520059002D00300031>"u8, "CANARY-01"));
        Assert.False(PdfText.Contains("BT [<43414E4152592D3032>] TJ ET"u8, "CANARY-01"));
    }

    [Fact]
    public void Rewrite_IsDeterministic()
    {
        byte[] input = Fixtures.CanaryAttachment();

        Assert.Equal(QpdfStructure.Rewrite(input), QpdfStructure.Rewrite(input));
    }

    [Fact]
    public void Read_InvalidData_ThrowsQpdfException()
    {
        Assert.Throws<QpdfException>(() => QpdfStructure.Rewrite(Encoding.ASCII.GetBytes("not a pdf")));
    }
}
