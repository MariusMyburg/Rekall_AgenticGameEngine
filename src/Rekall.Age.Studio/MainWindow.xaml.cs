using System.Windows;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = LoadInitialViewModel();
    }

    private static RekallAgeStudioViewModel LoadInitialViewModel()
    {
        var args = Environment.GetCommandLineArgs();
        var projectIndex = Array.IndexOf(args, "--project");
        var sceneIndex = Array.IndexOf(args, "--scene");
        if (projectIndex < 0 || projectIndex + 1 >= args.Length)
        {
            return new RekallAgeStudioViewModel(null);
        }

        var projectRoot = args[projectIndex + 1];
        var sceneName = sceneIndex >= 0 && sceneIndex + 1 < args.Length ? args[sceneIndex + 1] : "Main";
        RekallAgeWorkbenchModel? model = new RekallAgeWorkbenchModelBuilder()
            .BuildAsync(projectRoot, sceneName, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new RekallAgeStudioViewModel(model);
    }
}
