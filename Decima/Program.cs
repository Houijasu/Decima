using System.Reflection;

using Decima;

using Spectre.Console;
using Spectre.Console.Cli;

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

AnsiConsole.Write(new FigletText(nameof(Decima)).Centered().Color(Color.Cyan1));
AnsiConsole.Write(new Text($"v{version}", new Style(Color.Grey)).Centered());
AnsiConsole.WriteLine();
AnsiConsole.WriteLine();

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName(nameof(Decima));
    config.UseAssemblyInformationalVersion();
    config.ValidateExamples();
    config.UseStrictParsing();
    config.TrimTrailingPeriods(true);

    config.AddCommandsFromAssembly();
});

// If no arguments provided, show interactive menu
if (args.Length == 0)
{
    return app.Run(["menu"]);
}

return app.Run(args);
