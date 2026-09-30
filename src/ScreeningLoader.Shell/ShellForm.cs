using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ScreeningLoader.Core;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Shell;

/// <summary>
/// The application window.
/// </summary>
internal sealed class ShellForm : Form
{
    private const string DevServerVariable = "SCREENINGLOADER_DEV_SERVER";

    private readonly WebView2 webView = new() { Dock = DockStyle.Fill };
    private readonly ScreeningLoaderEngine engine;
    private readonly ErrorLog errorLog;

    private Bridge? bridge;
    private bool closeConfirmed;

    public ShellForm(ScreeningLoaderEngine engine, ErrorLog errorLog)
    {
        this.engine = engine;
        this.errorLog = errorLog;

        Text = "Candidate Screening Loader";
        Icon = LoadIcon();
        MinimumSize = new Size(960, 640);
        ClientSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(webView);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            await StartWebViewAsync();
        }
        catch (Exception ex)
        {
            errorLog.Write("webview-start", ex);

            // Without the WebView there is nowhere to show the error in the application's interface.
            MessageBox.Show(
                "No se ha podido iniciar la ventana. Comprueba que el runtime de WebView2 está instalado.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-analysis leaves CVs written but not archived, so it goes through the same
        // confirmation as the stop button, written in the interface rather than in a system dialog.
        if (!closeConfirmed && bridge is { IsRunning: true })
        {
            e.Cancel = true;
            bridge.RequestClose();

            return;
        }

        base.OnFormClosing(e);
    }

    private async Task StartWebViewAsync()
    {
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: UserDataFolder());

        await webView.EnsureCoreWebView2Async(environment);

        CoreWebView2 core = webView.CoreWebView2;
        string? devServer = Environment.GetEnvironmentVariable(DevServerVariable);

        if (devServer is null)
        {
            WebAssets assets = new(environment);

            core.AddWebResourceRequestedFilter($"{WebAssets.Origin}*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += assets.Serve;
        }

        string entry = devServer ?? WebAssets.EntryUrl;

        // The interface never navigates away from itself; anything that tries is a link that should not be there.
        core.NavigationStarting += (_, navigation) =>
            navigation.Cancel = !navigation.Uri.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
                && !navigation.Uri.StartsWith(WebAssets.Origin, StringComparison.OrdinalIgnoreCase);

        bridge = new Bridge(this, core, engine, errorLog, ForceClose);
        bridge.Attach();

        core.Navigate(entry);
    }

    private void ForceClose()
    {
        closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// The embedded icon, with every size; extracting it from the executable would return only the 32 px one.
    /// </summary>
    private static Icon? LoadIcon()
    {
        using Stream? resource = typeof(ShellForm).Assembly.GetManifestResourceStream("app.ico");

        return resource is null ? null : new Icon(resource);
    }

    /// <summary>
    /// The WebView profile folder, outside the install directory, which may be read-only.
    /// </summary>
    private static string UserDataFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CandidateScreeningLoader",
        "webview");
}
