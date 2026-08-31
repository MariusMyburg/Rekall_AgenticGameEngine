using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rekall.Age.Studio;

internal enum RekallAgeCodexApprovalChoice
{
    Deny,
    AllowOnce,
    AllowActionForSession,
    AllowAllForSession
}

internal sealed class CodexApprovalDialog : Window
{
    public RekallAgeCodexApprovalChoice Choice { get; private set; } = RekallAgeCodexApprovalChoice.Deny;

    public CodexApprovalDialog(string summary)
    {
        Title = "Codex approval";
        Width = 650;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "Codex requests permission",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedText"),
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        AddButton(buttons, "Deny", RekallAgeCodexApprovalChoice.Deny);
        AddButton(buttons, "Allow once", RekallAgeCodexApprovalChoice.AllowOnce);
        AddButton(buttons, "Allow this action for session", RekallAgeCodexApprovalChoice.AllowActionForSession);
        AddButton(buttons, "Allow all for session", RekallAgeCodexApprovalChoice.AllowAllForSession);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void AddButton(Panel panel, string label, RekallAgeCodexApprovalChoice choice)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(4, 0, 0, 0),
            MinWidth = choice == RekallAgeCodexApprovalChoice.Deny ? 76 : 116,
            Padding = new Thickness(12, 8, 12, 8)
        };
        button.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = true;
        };
        panel.Children.Add(button);
    }
}
