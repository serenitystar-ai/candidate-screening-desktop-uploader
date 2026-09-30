using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace ScreeningLoader.Shell;

/// <summary>
/// Serves the WebView the interface bundle embedded in the executable.
/// </summary>
internal sealed class WebAssets(CoreWebView2Environment environment)
{
    private const string ResourcePrefix = "web/";

    // A host reserved by RFC 2606: the request is handled here and never reaches the network.
    private const string HostName = "screening-loader.invalid";

    private static readonly Assembly s_assembly = typeof(WebAssets).Assembly;

    /// <summary>Origin the interface is served from.</summary>
    public static string Origin { get; } = $"https://{HostName}/";

    /// <summary>Document the window starts with.</summary>
    public static string EntryUrl { get; } = $"{Origin}index.html";

    /// <summary>
    /// Responds with the embedded resource that matches the request.
    /// </summary>
    public void Serve(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out Uri? uri) || uri.Host != HostName)
            return;

        string path = uri.AbsolutePath.Trim('/');

        if (path.Length == 0)
            path = "index.html";

        Stream? content = s_assembly.GetManifestResourceStream(ResourcePrefix + path);

        e.Response = content is null
            ? environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty)
            : environment.CreateWebResourceResponse(content, 200, "OK", $"Content-Type: {ContentType(path)}");
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}
