using DialogueMachine.Core;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return args.FirstOrDefault() == "runtime-export" ? RuntimeExportCommand.Run(args, Console.Out, cancellation.Token) : StoryTestCommand.Run(args, Console.Out, cancellation.Token);
