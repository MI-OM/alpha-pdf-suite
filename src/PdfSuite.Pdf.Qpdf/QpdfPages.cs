using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.Pdf.Qpdf;

/// <summary>
/// Page operations with qpdf, the structural engine (ADR-0002): the output is the first source with
/// its pages replaced by the listed ones. Its document-level content (information, metadata, outline,
/// forms, structure tree) and its encryption are kept. Pages from other sources are copied in with
/// everything they use. An outline entry that pointed at a page no longer there goes nowhere, and the
/// viewer lists it as "not a page in this document".
/// </summary>
public static class QpdfPages
{
    /// <exception cref="ArgumentException">No sources, no pages, or a page that does not exist.</exception>
    /// <exception cref="PageAssemblyNotAllowedException">A source forbids changing its pages.</exception>
    /// <exception cref="QpdfException">qpdf could not read a source or write the result.</exception>
    public static byte[] Rebuild(IReadOnlyList<PageSource> sources, IReadOnlyList<PageSpec> pages)
    {
        if (sources.Count == 0)
        {
            throw new ArgumentException("There is no source document.", nameof(sources));
        }

        if (pages.Count == 0)
        {
            throw new ArgumentException("A document needs at least one page.", nameof(pages));
        }

        var edits = new List<QpdfEdit>(sources.Count);
        try
        {
            for (int i = 0; i < sources.Count; i++)
            {
                QpdfEdit edit = QpdfEdit.Open(sources[i].Pdf, sources[i].Password);
                edits.Add(edit);

                // qpdf reports the document's restrictions even when the owner password was given.
                if (!edit.AllowsPageAssembly && !sources[i].OwnerAccess)
                {
                    throw new PageAssemblyNotAllowedException(i);
                }

                edit.PushInheritedPageAttributes();
            }

            QpdfObject[][] sourcePages = [.. edits.Select(e => Enumerable.Range(0, e.PageCount).Select(e.Page).ToArray())];
            foreach (PageSpec spec in pages)
            {
                if (!spec.IsBlank && (spec.Source < 0 || spec.Source >= edits.Count || spec.PageIndex < 0 || spec.PageIndex >= sourcePages[spec.Source].Length))
                {
                    throw new ArgumentException($"Document {spec.Source + 1} has no page {spec.PageIndex + 1}.", nameof(pages));
                }
            }

            QpdfEdit output = edits[0];
            foreach (QpdfObject page in sourcePages[0])
            {
                output.RemovePage(page);
            }

            // A page can be used only once; later uses get a copy of the first one's output page.
            var placed = new Dictionary<(int Source, int Page), QpdfObject>();
            foreach (PageSpec spec in pages)
            {
                QpdfObject added;
                if (spec.BlankSize is PageSize size)
                {
                    output.AddPage(output, BlankPage(output, size));
                    added = output.Page(output.PageCount - 1);
                }
                else if (placed.TryGetValue((spec.Source, spec.PageIndex), out QpdfObject first))
                {
                    output.AddPage(output, CopyPage(output, first));
                    added = output.Page(output.PageCount - 1);
                }
                else
                {
                    output.AddPage(edits[spec.Source], sourcePages[spec.Source][spec.PageIndex]);
                    added = output.Page(output.PageCount - 1);
                    placed[(spec.Source, spec.PageIndex)] = added;
                }

                if (spec.QuarterTurns % 4 != 0)
                {
                    Rotate(output, added, spec.QuarterTurns);
                }
            }

            return output.Save();
        }
        finally
        {
            foreach (QpdfEdit edit in edits)
            {
                edit.Dispose();
            }
        }
    }

    /// <summary>Turns a page by quarter turns clockwise, adding to any rotation it has (ISO 32000-1 §7.7.3.3, /Rotate).</summary>
    private static void Rotate(QpdfEdit edit, QpdfObject page, int quarterTurns)
    {
        int current = edit.Has(page, "/Rotate") ? edit.IntValue(edit.Get(page, "/Rotate")) : 0;
        int degrees = (((current + (quarterTurns * 90)) % 360) + 360) % 360;
        edit.Set(page, "/Rotate", edit.NewInteger(degrees));
    }

    private static QpdfObject BlankPage(QpdfEdit edit, PageSize size)
    {
        QpdfObject page = edit.NewDictionary();
        edit.Set(page, "/Type", edit.NewName("/Page"));
        QpdfObject box = edit.NewArray();
        foreach (double value in new[] { 0, 0, size.Width, size.Height })
        {
            edit.Append(box, edit.NewInteger((long)Math.Round(value)));
        }

        edit.Set(page, "/MediaBox", box);
        edit.Set(page, "/Resources", edit.NewDictionary());
        edit.Set(page, "/Contents", edit.NewStream([]));
        return edit.MakeIndirect(page);
    }

    // A shallow copy: the copy shares the original's content streams and resources, which PDF allows.
    // Annotations are copied one level down so each belongs to its own page; form-field widgets are
    // left off the copy, as a field cannot gain a widget without its field dictionary changing too.
    private static QpdfObject CopyPage(QpdfEdit edit, QpdfObject original)
    {
        QpdfObject copy = edit.MakeIndirect(edit.NewDictionary());
        foreach (string key in edit.Keys(original))
        {
            if (key is not ("/Parent" or "/Annots"))
            {
                edit.Set(copy, key, edit.Get(original, key));
            }
        }

        if (edit.Has(original, "/Annots") && edit.IsArray(edit.Get(original, "/Annots")))
        {
            QpdfObject annotations = edit.NewArray();
            foreach (QpdfObject annotation in edit.Items(edit.Get(original, "/Annots")))
            {
                if (!edit.IsDictionary(annotation) || edit.IsName(edit.Get(annotation, "/Subtype"), "/Widget"))
                {
                    continue;
                }

                QpdfObject copied = edit.NewDictionary();
                foreach (string key in edit.Keys(annotation))
                {
                    if (key is not ("/P" or "/Popup" or "/Parent" or "/IRT"))
                    {
                        edit.Set(copied, key, edit.Get(annotation, key));
                    }
                }

                edit.Set(copied, "/P", copy);
                edit.Append(annotations, edit.MakeIndirect(copied));
            }

            edit.Set(copy, "/Annots", annotations);
        }

        return copy;
    }
}
