using AngryMonkey.CloudLogin.Cli;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

return await CloudLoginCli.RunAsync(args, CliEnvironment.CreateDefault(), cancellation.Token);
