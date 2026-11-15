using RailWatch.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRailWatch(builder.Configuration);
builder.Services.AddHostedService<RailWorker>();
var host = builder.Build();
await Setup.InitializeAsync(host.Services, builder.Configuration);
await host.RunAsync();
