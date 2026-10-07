using DialogueMachine.Core;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return StoryTestCommand.Run(args, Console.Out, cancellation.Token);
