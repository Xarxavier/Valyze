using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Hangfire scheduler + ISuggestionEngine + price/FX refresh jobs land here
// once the first scheduled work exists. Keeping the host trivially runnable
// so wiring it into docker-compose is mechanical when the time comes.

var host = builder.Build();
await host.RunAsync();
