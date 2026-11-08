using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PdfSuite.Pdf.Abstractions;

namespace PdfSuite.App;

/// <summary>
/// The document sidebar's outline (bookmarks; View menu "Show Document Outline" in
/// docs/04-UI-COPY-AND-PRODUCT-RULES.md §3). Choosing an entry, by click or with the arrow keys,
/// goes to its page. Entries that go nowhere in this document, such as links to websites, are
/// listed and do nothing.
/// </summary>
internal sealed class OutlinePanel : Border
{
    private readonly TreeView _tree;
    private readonly TextBlock _empty;

    public OutlinePanel()
    {
        Width = 260;
        BorderThickness = new Thickness(0, 0, 1, 0);
        BorderBrush = Brushes.Gray;
        AutomationProperties.SetName(this, "Document outline");

        var heading = new TextBlock { Text = "Document outline", FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 6) };
        _empty = new TextBlock { Text = "This document has no outline.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), IsVisible = false };
        _tree = new TreeView();
        AutomationProperties.SetLabeledBy(_tree, heading);
        _tree.SelectionChanged += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: OutlineItem { PageIndex: int page } item })
            {
                EntryChosen?.Invoke(page, item.Top);
            }
        };

        var layout = new DockPanel();
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(_empty);
        layout.Children.Add(_tree);
        Child = layout;
    }

    /// <summary>Raised when an entry with a page is chosen: its page, and its point on the page if it has one.</summary>
    public event Action<int, double?>? EntryChosen;

    /// <summary>Shows a document's outline; null while none is open or it is still loading.</summary>
    public void Show(IReadOnlyList<OutlineItem>? outline)
    {
        _tree.Items.Clear();
        foreach (OutlineItem item in outline ?? [])
        {
            // Expanded one level: Avalonia's tree items do not offer UI Automation's expand and collapse
            // pattern (spike results, finding 55), so second-level entries are reachable at once.
            TreeViewItem node = Node(item);
            node.IsExpanded = item.Children.Count > 0;
            _tree.Items.Add(node);
        }

        _empty.IsVisible = outline is { Count: 0 };
    }

    // Items are built directly, with the title as a plain header and as the accessible name, so screen
    // readers announce each entry (a data template's text block is not used as the item's name).
    private static TreeViewItem Node(OutlineItem item)
    {
        string title = item.PageIndex is null ? $"{item.Title} (not a page in this document)" : item.Title;
        var node = new TreeViewItem { Header = title, Tag = item };
        if (item.PageIndex is null)
        {
            // Only these are greyed. Setting null for the others would draw their text in no colour.
            node.Foreground = Brushes.DimGray;
        }
        AutomationProperties.SetName(node, title);
        foreach (OutlineItem child in item.Children)
        {
            node.Items.Add(Node(child));
        }

        return node;
    }
}
