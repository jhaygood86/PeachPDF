using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PeachDrawing.Text.Brotli;
using PeachPDF.Demo.BlazorWasm;
using PeachPDF.Demo.BlazorWasm.Services;

// The culture is fixed to the invariant one by <InvariantGlobalization> in the csproj rather than assigned
// here - see the comment there for why that matters to a renderer whose input is CSS.

// System.IO.Compression.BrotliStream throws PlatformNotSupportedException in the browser, which would otherwise
// silently degrade the shared Unicode/hyphenation/dictionary data (PeachDrawing.Text.Data) to empty tables and skip
// every WOFF2 font as unreadable - see PeachDrawing.Text.Compression.BrotliDecompression's own remarks. Gated on
// OperatingSystem.IsBrowser() rather than called unconditionally so this Program.cs stays a correct example to copy
// for a host that isn't exclusively a browser build (Register() is itself harmless anywhere - the seam only ever
// takes effect where the BCL's own decoder would otherwise throw - but the gate documents *why* a WASM app wants
// this rather than leaving a reader to wonder).
if (OperatingSystem.IsBrowser())
{
    ManagedBrotliDecompressor.Register();
}

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Only ever used to fetch this app's own font assets - the rendered document never gets to make a request.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<FontLibrary>();
builder.Services.AddScoped<PdfRenderService>();

await builder.Build().RunAsync();
