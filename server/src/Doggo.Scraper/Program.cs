using Doggo.Infrastructure;
using Doggo.Scraper;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ScraperWorker>();

var host = builder.Build();
host.Run();
