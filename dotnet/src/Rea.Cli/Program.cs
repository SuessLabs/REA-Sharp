using Rea.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
using var input = args.Length == 1 && args[0] == "mcp"
    ? new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false, true), false, 4096, leaveOpen: true) : null;
return await CliApplication.RunAsync(args, Console.Out, Console.Error, cancellation.Token, input);
