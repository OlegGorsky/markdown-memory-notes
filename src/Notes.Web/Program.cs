using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MemoryNotes.Web;
using MemoryNotes.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<BrowserFileSystem>();
builder.Services.AddScoped<WebVaultSession>();

await builder.Build().RunAsync();
