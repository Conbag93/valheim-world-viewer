using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ValheimDiagrams.Client;
using ValheimDiagrams.Client.Services;

// A standalone app: everything, including reading save files, runs in the browser.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<WorldStore>();

await builder.Build().RunAsync();
