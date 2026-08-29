using System.IO;
using System.Windows;

namespace Rekall.Age.Studio;

public partial class CreateProjectDialog : Window
{
    public CreateProjectDialog(string initialParentFolder)
    {
        InitializeComponent();
        ParentFolderBox.Text = initialParentFolder;
        ProjectNameBox.Focus();
        ProjectNameBox.SelectAll();
    }

    internal RekallAgeCreateProjectRequest? Request { get; private set; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where to create the project",
            Multiselect = false
        };
        if (Directory.Exists(ParentFolderBox.Text)) dialog.InitialDirectory = ParentFolderBox.Text;
        if (dialog.ShowDialog(this) == true) ParentFolderBox.Text = dialog.FolderName;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (!RekallAgeCreateProjectRequest.TryCreate(
                ParentFolderBox.Text,
                ProjectNameBox.Text,
                SceneNameBox.Text,
                out var request,
                out var error))
        {
            ValidationText.Text = error;
            return;
        }

        if (Directory.Exists(request!.ProjectRoot) && Directory.EnumerateFileSystemEntries(request.ProjectRoot).Any())
        {
            ValidationText.Text = "That project folder already exists and is not empty. Choose another name or location.";
            return;
        }

        Request = request;
        DialogResult = true;
    }
}
