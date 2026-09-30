using ScreeningLoader.Core;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Shell;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // The title bar is the only visible part of the frame, and a light one over a dark interface stands out.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        ErrorLog errorLog = new(ErrorLog.DefaultDirectory);

        using ScreeningLoaderEngine engine = new(new ScreeningLoaderOptions(), errorLog);

        Application.Run(new ShellForm(engine, errorLog));
    }
}
