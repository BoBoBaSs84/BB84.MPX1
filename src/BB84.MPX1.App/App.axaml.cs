using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BB84.MPX1.App.Services;
using BB84.MPX1.App.ViewModels;
using BB84.MPX1.App.Views;
using BB84.MPX1.Midi.IO;
using BB84.MPX1.Midi.Learning;

namespace BB84.MPX1.App;

/// <summary>
/// The Avalonia application root and composition root.
/// </summary>
public sealed partial class App : Application
{
    /// <inheritdoc/>
    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? window = null;
            FileProvider files = new();
            DirectoryProvider directories = new();
            MainWindowViewModel viewModel = new(
                MidiPortCatalog.CreateDefault(),
                new SettingsStore(AppPaths.SettingsFile, files, directories),
                new FileParameterDatabaseStore(AppPaths.DatabaseDirectory, files, directories),
                new AvaloniaUiDispatcher(),
                new AvaloniaUserDialogs(() => window),
                TimeProvider.System);
            desktop.MainWindow = window = new MainWindow(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
