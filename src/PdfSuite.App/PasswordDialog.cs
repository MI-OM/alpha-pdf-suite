using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PdfSuite.App;

/// <summary>
/// Asks for a document's password (docs/04-UI-COPY-AND-PRODUCT-RULES.md §6). It stays open after a
/// wrong password, saying so, until the document opens or the user cancels. The password is passed
/// to <c>tryUnlock</c> and kept nowhere else; it is never logged (decisions for review, D2).
/// </summary>
internal sealed class PasswordDialog : Window
{
    private readonly Func<string, Task<bool>> _tryUnlock;
    private readonly TextBox _password;
    private readonly TextBlock _error;
    private readonly Button _unlock;
    private readonly Button _cancel;

    public PasswordDialog(string documentName, Func<string, Task<bool>> tryUnlock)
    {
        _tryUnlock = tryUnlock;
        Title = "This document is password-protected";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var heading = new TextBlock { Text = "This document is password-protected", FontSize = 16, FontWeight = FontWeight.SemiBold };
        var name = new TextBlock { Text = documentName, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var body = new TextBlock
        {
            Text = "Enter the document password to continue. The password is used to open this document and should not be shared.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var label = new TextBlock { Text = "Password", Margin = new Thickness(0, 16, 0, 4) };
        _password = new TextBox { PasswordChar = '•', RevealPassword = false };
        AutomationProperties.SetLabeledBy(_password, label);
        _password.TextChanged += (_, _) => _error!.IsVisible = false;

        // Announced at once when it appears; it is text, not colour alone.
        _error = new TextBlock
        {
            Text = "The password could not unlock this document. Check the password and try again.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA4, 0x00, 0x00)),
            IsVisible = false,
        };
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);

        _unlock = new Button { Content = "Unlock PDF", IsDefault = true };
        _unlock.Click += async (_, _) => await UnlockAsync();
        _cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        _cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(_unlock);
        buttons.Children.Add(_cancel);

        var content = new StackPanel { Margin = new Thickness(20) };
        foreach (Control control in new Control[] { heading, name, body, label, _password, _error, buttons })
        {
            content.Children.Add(control);
        }

        Content = content;
        Opened += (_, _) => _password.Focus();
    }

    private async Task UnlockAsync()
    {
        string password = _password.Text ?? string.Empty;
        _unlock.IsEnabled = false;
        _cancel.IsEnabled = false;
        try
        {
            if (await _tryUnlock(password))
            {
                Close(true);
                return;
            }
        }
        finally
        {
            _unlock.IsEnabled = true;
            _cancel.IsEnabled = true;
        }

        _error.IsVisible = true;
        _password.SelectAll();
        _password.Focus();
    }
}
