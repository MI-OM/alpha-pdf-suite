using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.App;

/// <summary>
/// Document Properties (File menu in docs/04-UI-COPY-AND-PRODUCT-RULES.md §3): what the document
/// says about itself, its size, pages, version, security and permissions. Read-only.
/// </summary>
internal sealed class PropertiesDialog : Window
{
    private const string NotSet = "Not set";

    public PropertiesDialog(string fileName, long fileSize, IReadOnlyList<PageSize> pageSizes, DocumentInfo info)
    {
        Title = "Document Properties";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var rows = new List<(string Label, string Value)>
        {
            ("File", fileName),
            ("File size", FileSize(fileSize)),
            ("Pages", pageSizes.Count.ToString("N0", CultureInfo.CurrentCulture)),
            ("Page size", PageSizeText(pageSizes)),
            ("PDF version", info.PdfVersion ?? "Unknown"),
            ("Title", Value(info, "Title")),
            ("Author", Value(info, "Author")),
            ("Subject", Value(info, "Subject")),
            ("Keywords", Value(info, "Keywords")),
            ("Created", Date(info, "CreationDate")),
            ("Modified", Date(info, "ModDate")),
            ("Application", Value(info, "Creator")),
            ("PDF producer", Value(info, "Producer")),
            ("Tagged PDF", info.Tagged ? "Yes" : "No"),
            ("Security", info.Encrypted ? "Encrypted" : "Not encrypted"),
        };

        if (info.Encrypted)
        {
            rows.Add(("Printing", Allowed(info.Permissions.Print)));
            rows.Add(("Copying text", Allowed(info.Permissions.CopyText)));
            rows.Add(("Changing the document", Allowed(info.Permissions.Modify)));
            rows.Add(("Comments and form filling", Allowed(info.Permissions.AnnotateAndFillForms)));
            rows.Add(("Page assembly", Allowed(info.Permissions.Assemble)));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(20, 20, 20, 0) };
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, Margin = new Thickness(0, 3, 16, 3), Foreground = Brushes.DimGray };
            var value = new SelectableTextBlock { Text = rows[i].Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3) };
            AutomationProperties.SetLabeledBy(value, label);
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20) };
        close.Click += (_, _) => Close();

        var layout = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        layout.Children.Add(new ScrollViewer { Content = grid });
        Content = layout;
        Opened += (_, _) => close.Focus();
    }

    private static string Value(DocumentInfo info, string key) =>
        info.Metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : NotSet;

    // In the reader's own date format; the document's text as written if it is not a valid PDF date.
    private static string Date(DocumentInfo info, string key)
    {
        if (!info.Metadata.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text))
        {
            return NotSet;
        }

        return PdfDate.TryParse(text, out DateTimeOffset date)
            ? date.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)
            : text;
    }

    private static string Allowed(bool allowed) => allowed ? "Allowed" : "Not allowed";

    private static string FileSize(long bytes)
    {
        string exact = bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
        return bytes < 1024 ? exact
            : bytes < 1024 * 1024 ? $"{(bytes / 1024.0).ToString("N1", CultureInfo.CurrentCulture)} KB ({exact})"
            : $"{(bytes / (1024.0 * 1024.0)).ToString("N1", CultureInfo.CurrentCulture)} MB ({exact})";
    }

    // The first page's size in inches and millimetres; noted when pages differ.
    private static string PageSizeText(IReadOnlyList<PageSize> sizes)
    {
        if (sizes.Count == 0)
        {
            return "Unknown";
        }

        PageSize first = sizes[0];
        string text = string.Create(CultureInfo.CurrentCulture,
            $"{first.Width / 72:0.##} × {first.Height / 72:0.##} in ({first.Width / 72 * 25.4:0} × {first.Height / 72 * 25.4:0} mm)");
        bool varies = sizes.Any(s => Math.Abs(s.Width - first.Width) > 0.5 || Math.Abs(s.Height - first.Height) > 0.5);
        return varies ? $"{text} on the first page; pages differ in size" : text;
    }
}
