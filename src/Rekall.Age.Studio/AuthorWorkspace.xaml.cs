using System.Windows;
using System.Windows.Controls;

namespace Rekall.Age.Studio;

public partial class AuthorWorkspace : UserControl
{
    public AuthorWorkspace()
    {
        InitializeComponent();
    }

    private async void OnApplyOpenAiApiKeyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RekallAgeStudioViewModel viewModel) return;
        var sessionKey = OpenAiApiKeyInput.Password;
        OpenAiApiKeyInput.Clear();
        await viewModel.ApplyOpenAiApiKeyAsync(sessionKey);
    }
}
