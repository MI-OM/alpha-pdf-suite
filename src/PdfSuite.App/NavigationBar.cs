using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace PdfSuite.App;

/// <summary>
/// Page and zoom controls for the reader toolbar (docs/04-UI-COPY-AND-PRODUCT-RULES.md §4): previous
/// and next page, the current page out of the total, zoom out and in, and the View menu's Fit Width,
/// Fit Page and Actual Size. Typing a page number and pressing Enter goes to it; Ctrl+G focuses it.
/// </summary>
internal sealed class NavigationBar : DockPanel
{
    private readonly DocumentView _view;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly TextBox _page;
    private readonly TextBlock _total;
    private readonly Control[] _documentControls;

    public NavigationBar(DocumentView view)
    {
        _view = view;
        Margin = new Thickness(8, 0, 8, 6);

        _previous = Button("Previous", "Go to the previous page", "Previous page", () => GoTo(_view.CurrentPage - 1));
        _page = new TextBox { Width = 64, HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_page, "Current page");
        ToolTip.SetTip(_page, "Type a page number, then press Enter");
        _page.AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Tunnel);
        _page.LostFocus += (_, _) => Update();
        _total = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        _next = Button("Next", "Go to the next page", "Next page", () => GoTo(_view.CurrentPage + 1));

        Button zoomOut = Button("−", "Zoom out", "Zoom out", () => _view.SetZoom(_view.Zoom * 0.8), 16);
        Button zoomIn = Button("+", "Zoom in", "Zoom in", () => _view.SetZoom(_view.Zoom * 1.25));
        Button fitWidth = Button("Fit Width", "Fit the page to the window's width (Ctrl+2)", "Fit Width", _view.FitWidth, 8);
        Button fitPage = Button("Fit Page", "Fit the whole page in the window (Ctrl+0)", "Fit Page", _view.FitPage);
        Button actualSize = Button("Actual Size", "Show the page at its actual size (Ctrl+1)", "Actual Size", () => _view.SetZoom(1));

        _documentControls = [_previous, _page, _next, zoomOut, zoomIn, fitWidth, fitPage, actualSize];
        foreach (Control control in new Control[] { _previous, _page, _total, _next, zoomOut, zoomIn, fitWidth, fitPage, actualSize })
        {
            SetDock(control, Dock.Left);
            Children.Add(control);
        }

        Children.Add(new Panel());
        _view.StatusChanged += Update;
        Update();
    }

    /// <summary>Puts the cursor in the page number box, with the number selected.</summary>
    public void FocusPageNumber()
    {
        _page.Focus(NavigationMethod.Tab);
        _page.SelectAll();
    }

    private static Button Button(string content, string tip, string name, Action click, double leftMargin = 4)
    {
        var button = new Button { Content = content, Margin = new Thickness(leftMargin, 0, 0, 0), MinWidth = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private void GoTo(int pageIndex)
    {
        if (_view.PageCount > 0)
        {
            _view.ScrollToPage(Math.Clamp(pageIndex, 0, _view.PageCount - 1));
        }
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (int.TryParse(_page.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int number))
            {
                GoTo(number - 1);
                _view.Focus();
            }

            Update(force: true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Update(force: true);
            _view.Focus();
        }
    }

    private void Update() => Update(force: false);

    private void Update(bool force)
    {
        int count = _view.PageCount;
        foreach (Control control in _documentControls)
        {
            control.IsEnabled = count > 0;
        }

        _total.Text = count > 0 ? string.Create(CultureInfo.CurrentCulture, $"of {count:N0}") : string.Empty;

        // Not while someone is typing a page number.
        if (force || !_page.IsFocused)
        {
            _page.Text = count > 0 ? (_view.CurrentPage + 1).ToString(CultureInfo.CurrentCulture) : string.Empty;
        }

        _previous.IsEnabled = count > 0 && _view.CurrentPage > 0;
        _next.IsEnabled = count > 0 && _view.CurrentPage < count - 1;
    }
}
