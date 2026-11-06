namespace PdfSuite.Pdf.Abstractions;

// Data for whole-document operations: encryption (qpdf) and redaction. They are engine-neutral, so
// the app can ask the worker for these operations without referencing either engine.

/// <summary>What a user may do with an encrypted document without the owner password.</summary>
public sealed record PdfPermissions(
    bool Print = true,
    bool CopyText = true,
    bool Modify = false,
    bool AnnotateAndFillForms = true,
    bool Assemble = false);

/// <summary>An area to remove: a rectangle in points from the top-left corner of the page as displayed.</summary>
public readonly record struct RedactionArea(int PageIndex, PageRect Rect);

public enum RedactionCheck
{
    /// <summary>One revision and nothing after its end-of-file marker. Earlier revisions keep whatever they held.</summary>
    SingleRevision,

    /// <summary>As many pages as the source, each the same size.</summary>
    PagesMatchSource,

    /// <summary>No document information, metadata, actions, bookmarks, attachments, forms, structure tree or layers.</summary>
    NoDocumentLevelContent,

    /// <summary>No page objects outside the page tree, such as an original page copied in through a link.</summary>
    NoStrayPages,

    /// <summary>Each redacted page is a single image: no text, vector art or annotations.</summary>
    RedactedPagesAreImages,

    /// <summary>No covered text, and no extra term, is left as page text or in any string or stream in the file.</summary>
    TextRemoved,

    /// <summary>Every area renders as solid black.</summary>
    AreasCovered,
}

/// <summary>A failed check. The message may quote the document's text, so it is shown to the user and never logged.</summary>
public sealed record VerificationFailure(RedactionCheck Check, string Message);

/// <summary>The verifier's findings. Warnings may also quote the document's text.</summary>
public sealed record VerificationReport(IReadOnlyList<VerificationFailure> Failures, IReadOnlyList<string> Warnings)
{
    public bool Passed => Failures.Count == 0;

    public bool Failed(RedactionCheck check) => Failures.Any(f => f.Check == check);
}

public sealed record RedactionResult(byte[] Pdf, VerificationReport Report);

/// <summary>
/// The rebuilt file did not pass verification, so it was not returned. The message names only the
/// failed checks, because exception messages end up in logs; <see cref="Report"/> has the details.
/// </summary>
public sealed class RedactionFailedException(VerificationReport report)
    : Exception("The redacted file failed verification: " + string.Join(", ", report.Failures.Select(f => f.Check).Distinct()) + ".")
{
    public VerificationReport Report { get; } = report;
}
