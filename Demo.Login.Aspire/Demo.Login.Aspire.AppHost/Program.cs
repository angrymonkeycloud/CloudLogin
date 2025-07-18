var builder = DistributedApplication.CreateBuilder(args);

//var cosmos = builder.AddAzureCosmosDB("cosmos").RunAsEmulator(emulator => { emulator.WithDataVolume().WithLifetime(ContainerLifetime.Persistent); });

//var database = cosmos.AddCosmosDatabase("cosmos-database", "users");

//var container = database
//    .AddContainer("cosmos-container", "/PartitionKey", "data");

builder.AddProject<Projects.Demo_Login_Standalone>("webfrontend", "aspire")
    .WithHttpsEndpoint(port: 64401, targetPort: 64400);
    //.WithReference(cosmos)
    //.WaitFor(cosmos);

builder.Build().Run();
