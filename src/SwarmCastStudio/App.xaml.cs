using System.Linq;
using System.Windows;
using SwarmCastStudio.Services;

namespace SwarmCastStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument =>
                argument.StartsWith("--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            int exitCode;
            try
            {
                exitCode = SmokeRunner.RunAsync(e.Args, AppContext.BaseDirectory)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                exitCode = 1;
            }

            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
