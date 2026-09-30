using ScreeningLoader.Core;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Shell;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Lo único visible del marco es la barra de título, y en claro sobre una interfaz oscura canta.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001

        ErrorLog errorLog = new(ErrorLog.DefaultDirectory);

        using ScreeningLoaderEngine engine = new(new ScreeningLoaderOptions(), errorLog);

        Application.Run(new ShellForm(engine, errorLog));
    }
}
