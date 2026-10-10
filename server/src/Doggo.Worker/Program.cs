using Doggo.Infrastructure;
using Doggo.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
