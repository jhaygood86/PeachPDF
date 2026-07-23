using PeachPDF;

namespace PeachPDF.Cli;

/// <summary>
/// The CLI entry point: parses the command line, dispatches the informational actions
/// (<c>--help</c>/<c>--version</c>/<c>--show-license</c>/<c>--credits</c>), reports parse errors, and
/// otherwise renders the inputs via <see cref="CliRunner"/>.
/// </summary>
internal static class CliProgram
{
    public static async Task<int> MainAsync(string[] args)
    {
        var options = ArgumentParser.Parse(args);

        switch (options.Action)
        {
            case CliAction.Help:
                Console.WriteLine(HelpText);
                return 0;
            case CliAction.Version:
                Console.WriteLine(PeachPdfProductInfo.Generator);
                return 0;
            case CliAction.ShowLicense:
                Console.WriteLine(LicenseInfo.License);
                return 0;
            case CliAction.Credits:
                Console.WriteLine(LicenseInfo.Credits);
                return 0;
        }

        if (options.Errors.Count > 0)
        {
            foreach (var error in options.Errors)
            {
                await Console.Error.WriteLineAsync($"error: {error}");
            }

            await Console.Error.WriteLineAsync("Try 'peachpdf --help' for usage information.");
            return 1;
        }

        return await CliRunner.RunAsync(options);
    }

    private const string HelpText =
        """
        peachpdf - render HTML to PDF (PeachPDF)

        Usage:
          peachpdf [OPTIONS] FILES... [-o OUTPUT.pdf]

        FILES are HTML files, HTTP(S) URLs, MHTML (.mht/.mhtml) archives, or '-' for
        standard input. Multiple inputs are combined into a single PDF. If -o is
        omitted the output is the first input's name with a .pdf extension; '-o -'
        writes the PDF to standard output.

        General:
          -h, --help                 Show this help and exit.
              --version              Show version information and exit.
              --show-license         Show the license and exit.
              --credits              Show the license and third-party acknowledgements.
          -v, --verbose              Log informative messages to stderr.
              --debug                Log debug messages to stderr.
              --log=FILE             Append log messages to FILE.

        Input:
          -l, --input-list=FILE      Read a newline-separated list of inputs from FILE.
              --baseurl=URL          Base URL for resolving relative resources.
              --no-network           Disable network (HTTP) resource access.
              --no-local-files       Disable local file resource access.

        CSS:
          -s, --style=FILE           Apply a user style sheet (repeatable, last wins).
              --media=MEDIA          CSS media type to render (default: print).
              --no-default-style     Ignore the default (user-agent) style sheet.
              --no-author-style      Ignore the document's own style sheets.
              --page-size=SIZE       Page size (e.g. A4, letter, "210mm 297mm").
              --page-margin=MARGIN   Page margin (1-4 CSS lengths, e.g. 20mm).

        PDF output:
          -o, --output=FILE          Output PDF file ('-' for standard output).
              --pdf-title=TITLE      Set the PDF title.
              --pdf-author=AUTHOR    Set the PDF author.
              --pdf-subject=SUBJECT  Set the PDF subject.
              --pdf-keywords=WORDS   Set the PDF keywords.
              --pdf-creator=CREATOR  Set the PDF creator.
              --pdf-lang=LANG        Set the PDF language (/Lang).
              --tagged-pdf           Emit a tagged (PDF/UA) structure tree.
              --no-compress          Do not compress PDF content streams.

        Network (HTTP inputs and resources):
              --http-timeout=SEC     HTTP request timeout in seconds.
              --http-header=HEADER   Add a request header "Name: value" (repeatable).
              --user-agent=UA        Set the User-Agent header.
              --auth-user=USER       HTTP basic-auth user name.
              --auth-password=PASS   HTTP basic-auth password.
              --http-proxy=PROXY     HTTP proxy server.
              --insecure             Disable TLS certificate verification.
        """;
}
