using System.Windows;
using System.Windows.Controls;

namespace Rekall.Age.Studio;

public partial class GenerateTextureDialog : Window
{
    public GenerateTextureDialog() => InitializeComponent();

    internal RekallAgeStudioTextureGenerationOptions? Options { get; private set; }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        var prompt = PromptBox.Text.Trim();
        if (prompt.Length == 0)
        {
            ValidationText.Text = "Describe the texture you want to generate.";
            PromptBox.Focus();
            return;
        }

        Options = new(
            prompt,
            string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? null : DisplayNameBox.Text.Trim(),
            Selected(RoleBox),
            Selected(SizeBox),
            Selected(QualityBox),
            SeamlessBox.IsChecked == true);
        DialogResult = true;
    }

    private static string Selected(ComboBox box) =>
        ((ComboBoxItem)box.SelectedItem).Content?.ToString() ?? string.Empty;
}
