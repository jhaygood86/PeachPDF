using System.Net;

namespace PeachBrackets.Apple.Example;

/// <summary>Fetching the page of a web showcase.</summary>
internal static class WebPage
{
    /// <summary>The page at an address, as it was fetched, or a short page that says why it could not be.</summary>
    public static async Task<(string Html, bool Fetched)> FetchAsync(string address)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PeachBrackets.HostResourceLoader.UserAgent);
            return (await client.GetStringAsync(address), true);
        }
        catch (Exception exception)
        {
            return ($"<p style='font:16px sans-serif;padding:16pt'>Could not fetch {address}: {WebUtility.HtmlEncode(exception.Message)}</p>", false);
        }
    }

    /// <summary>The fetched page made ready for the view, which runs no scripts: a base address and the classes the site's startup script would have set.</summary>
    public static string Prepare(string source, Showcase showcase, bool dark)
    {
        try
        {
            return PageRewriter.Rewrite(source, showcase.Path, WebPageTraits.RootClasses(showcase.Slug, dark));
        }
        catch (Exception exception)
        {
            return $"<p style='font:16px sans-serif;padding:16pt'>Could not prepare {showcase.Path}: {WebUtility.HtmlEncode(exception.Message)}</p>";
        }
    }
}
