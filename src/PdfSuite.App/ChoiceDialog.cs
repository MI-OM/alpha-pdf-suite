using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PdfSuite.App;

/// <summary>
/// A question with a few answers, such as the copy rules' "Save your changes?" (§7). Returns the
/// chosen answer's index; closing the window or pressing Escape picks <c>cancelIndex</c>.
/// </summary>
internal sealed class ChoiceDialog : Window
{
    public ChoiceDialog(string heading, string body, IReadOnlyList<string> answers, int defaultIndex, int cancelIndex)
    {
        Title = heading;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Spacing = 8 };
        for (int i = 0; i < answers.Count; i++)
        {
            int index = i;
            var button = new Button { Content = answers[i], IsDefault = i == defaultIndex, IsCancel = i == cancelIndex };
            button.Click += (_, _) => Close(index);
            buttons.Children.Add(button);
        }

        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(buttons);
        Content = content;
        Opened += (_, _) => (buttons.Children[defaultIndex] as Button)?.Focus();
    }

    /// <summary>Shows the dialog and returns the chosen answer's index, or <paramref name="cancelIndex"/> if it was closed.</summary>
    public static async Task<int> AskAsync(Window owner, string heading, string body, IReadOnlyList<string> answers, int defaultIndex, int cancelIndex)
    {
        int? choice = await new ChoiceDialog(heading, body, answers, defaultIndex, cancelIndex).ShowDialog<int?>(owner);
        return choice ?? cancelIndex;
    }
}
