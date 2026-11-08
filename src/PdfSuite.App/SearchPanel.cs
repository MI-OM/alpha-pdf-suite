using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Media;

namespace PdfSuite.App;

/// <summary>
/// The find bar (docs/04-UI-COPY-AND-PRODUCT-RULES.md §5). It only collects the query and options
/// and shows a status line; <see cref="MainWindow"/> runs the search.
/// Enter finds the next match and Shift+Enter the previous one; Escape closes the bar.
/// </summary>
internal sealed class SearchPanel : Border
{
    private readonly TextBox _query;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWords;
    private readonly TextBlock _status;

    public SearchPanel()
    {
        Padding = new Thickness(8, 4);
        BorderThickness = new Thickness(0, 1, 0, 1);
        BorderBrush = Brushes.Gray;
        AutomationProperties.SetName(this, "Find in document");

        var heading = new TextBlock { Text = "Find in document", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _query = new TextBox { Width = 260, PlaceholderText = "Enter a word or phrase", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLabeledBy(_query, heading);
        _query.TextChanged += (_, _) => QueryChanged?.Invoke();

        // Tunnelling, so Enter and Escape are seen before the text box acts on them.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        var previous = new Button { Content = "Previous match", Margin = new Thickness(8, 0, 0, 0) };
        previous.Click += (_, _) => FindRequested?.Invoke(false);
        var next = new Button { Content = "Next match", Margin = new Thickness(4, 0, 0, 0) };
        next.Click += (_, _) => FindRequested?.Invoke(true);

        _matchCase = new CheckBox { Content = "Match case", Margin = new Thickness(12, 0, 0, 0) };
        _matchCase.IsCheckedChanged += (_, _) => OptionsChanged?.Invoke();
        _wholeWords = new CheckBox { Content = "Whole words", Margin = new Thickness(8, 0, 0, 0) };
        _wholeWords.IsCheckedChanged += (_, _) => OptionsChanged?.Invoke();

        var close = new Button { Content = "Close", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(close, "Close the search bar");
        close.Click += (_, _) => CloseRequested?.Invoke();

        // Announced by screen readers when it changes, so match counts are heard without moving focus.
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        var row = new DockPanel();
        foreach (Control control in new Control[] { heading, _query, previous, next, _matchCase, _wholeWords })
        {
            DockPanel.SetDock(control, Dock.Left);
            row.Children.Add(control);
        }

        DockPanel.SetDock(close, Dock.Right);
        row.Children.Add(close);
        row.Children.Add(_status);
        Child = row;
    }

    /// <summary>Raised to find the next match (true) or the previous one (false).</summary>
    public event Action<bool>? FindRequested;

    public event Action? QueryChanged;

    public event Action? OptionsChanged;

    public event Action? CloseRequested;

    public string Query => _query.Text ?? string.Empty;

    public bool MatchCase => _matchCase.IsChecked == true;

    public bool WholeWords => _wholeWords.IsChecked == true;

    public string Status
    {
        get => _status.Text ?? string.Empty;
        set => _status.Text = value;
    }

    /// <summary>Puts the cursor in the query box, with its text selected for replacing.</summary>
    public void FocusQuery()
    {
        _query.Focus(NavigationMethod.Tab);
        _query.SelectAll();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        }
        else if (e.Key == Key.Enter && e.Source is Visual source && (source == _query || _query.IsVisualAncestorOf(source)))
        {
            e.Handled = true;
            FindRequested?.Invoke(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }
}
