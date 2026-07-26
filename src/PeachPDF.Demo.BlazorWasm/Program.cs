using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PeachPDF.Demo.BlazorWasm;
using PeachPDF.Demo.BlazorWasm.Services;

// The culture is fixed to the invariant one by <InvariantGlobalization> in the csproj rather than assigned
// here - see the comment there for why that matters to a renderer whose input is CSS.

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Only ever used to fetch this app's own font assets - the rendered document never gets to make a request.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<FontLibrary>();
builder.Services.AddScoped<PdfRenderService>();

await builder.Build().RunAsync();
