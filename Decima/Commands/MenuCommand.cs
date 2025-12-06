namespace Decima.Commands;

using System.ComponentModel;
using System.Reflection;

using Spectre.Console;
using Spectre.Console.Cli;

/// <summary>
/// Interactive menu command that dynamically discovers and presents all available commands.
/// </summary>
public sealed class MenuCommand : Command<MenuCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        while (true)
        {
            AnsiConsole.Clear();
            PrintHeader();

            var commands = DiscoverCommands();
            var choice = ShowMainMenu(commands);

            if (choice == null || choice == "Exit")
            {
                AnsiConsole.MarkupLine("[dim]Goodbye![/]");
                return 0;
            }

            var selectedCommand = commands.First(c => c.DisplayName == choice);
            var result = ExecuteCommandWithMenu(selectedCommand, cancellationToken);

            if (result != -1) // -1 means go back to menu
            {
                AnsiConsole.MarkupLine("\n[dim]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
    }

    private static void PrintHeader()
    {
        AnsiConsole.Write(new FigletText("Decima").Centered().Color(Color.Cyan1));
        AnsiConsole.Write(new Text("ML-Powered Sudoku Solver", new Style(Color.Grey)).Centered());
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    private static List<CommandInfo> DiscoverCommands()
    {
        var commandTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Name.EndsWith("Command")
                        && t.IsAssignableTo(typeof(ICommand))
                        && t != typeof(MenuCommand)) // Exclude self
            .ToList();

        var commands = new List<CommandInfo>();

        foreach (var type in commandTypes)
        {
            var name = type.Name.Replace("Command", string.Empty);
            var description = type.GetCustomAttribute<DescriptionAttribute>()?.Description
                              ?? GetCommandDescription(name);

            var settingsType = type.GetNestedType("Settings");
            var options = settingsType != null ? DiscoverOptions(settingsType) : [];

            commands.Add(new CommandInfo(name, description, type, options));
        }

        return commands.OrderBy(c => c.Name).ToList();
    }

    private static string GetCommandDescription(string commandName)
    {
        return commandName.ToLower() switch
        {
            "train" => "Train the neural network model",
            "solve" => "Solve a Sudoku puzzle",
            "play" => "Play Sudoku interactively",
            _ => $"Run the {commandName} command"
        };
    }

    private static List<OptionInfo> DiscoverOptions(Type settingsType)
    {
        var options = new List<OptionInfo>();

        foreach (var prop in settingsType.GetProperties())
        {
            var optionAttr = prop.GetCustomAttribute<CommandOptionAttribute>();
            var argAttr = prop.GetCustomAttribute<CommandArgumentAttribute>();

            if (optionAttr == null && argAttr == null)
                continue;

            var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
            var defaultValue = prop.GetCustomAttribute<DefaultValueAttribute>()?.Value;
            var isRequired = argAttr != null;

            var name = optionAttr?.LongNames.FirstOrDefault()
                       ?? optionAttr?.ShortNames.FirstOrDefault()?.ToString()
                       ?? prop.Name;

            // Build template from option attributes
            var template = "";
            if (optionAttr != null)
            {
                var shortName = optionAttr.ShortNames.FirstOrDefault();
                var longName = optionAttr.LongNames.FirstOrDefault();
                template = shortName != null && longName != null
                    ? $"-{shortName}|--{longName}"
                    : longName != null ? $"--{longName}" : $"-{shortName}";
            }

            options.Add(new OptionInfo(
                Name: name,
                Template: template,
                Description: description,
                PropertyType: prop.PropertyType,
                DefaultValue: defaultValue,
                IsRequired: isRequired,
                Property: prop
            ));
        }

        return options;
    }

    private static string ShowMainMenu(List<CommandInfo> commands)
    {
        var choices = commands.Select(c => c.DisplayName).ToList();
        choices.Add("Exit");

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]What would you like to do?[/]")
                .PageSize(10)
                .HighlightStyle(new Style(Color.Cyan1))
                .AddChoices(choices));
    }

    private static int ExecuteCommandWithMenu(CommandInfo command, CancellationToken cancellationToken)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[bold cyan]{command.Name}[/]");
        AnsiConsole.MarkupLine($"[dim]{command.Description}[/]");
        AnsiConsole.WriteLine();

        // If command has options, show configuration menu
        if (command.Options.Count > 0)
        {
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Choose an action:[/]")
                    .HighlightStyle(new Style(Color.Cyan1))
                    .AddChoices(["Run with defaults", "Configure options", "Back to menu"]));

            if (action == "Back to menu")
                return -1;

            var args = new List<string> { command.Name.ToLower() };

            if (action == "Configure options")
            {
                args.AddRange(ConfigureOptions(command));
            }

            return RunCommand(args.ToArray());
        }
        else
        {
            return RunCommand([command.Name.ToLower()]);
        }
    }

    private static List<string> ConfigureOptions(CommandInfo command)
    {
        var args = new List<string>();
        var configuredValues = new Dictionary<string, object?>();

        // Initialize with defaults
        foreach (var option in command.Options)
        {
            configuredValues[option.Name] = option.DefaultValue;
        }

        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine($"[bold cyan]{command.Name} Configuration[/]");
            AnsiConsole.WriteLine();

            // Show current configuration
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey);
            table.AddColumn("[cyan]Option[/]");
            table.AddColumn("[cyan]Value[/]");
            table.AddColumn("[dim]Description[/]");

            foreach (var option in command.Options)
            {
                var value = configuredValues[option.Name];
                var valueStr = value?.ToString() ?? "[dim]<not set>[/]";
                var isDefault = Equals(value, option.DefaultValue);
                
                if (isDefault)
                    valueStr = $"[dim]{valueStr}[/]";
                else
                    valueStr = $"[green]{valueStr}[/]";

                table.AddRow(
                    option.Name,
                    valueStr,
                    $"[dim]{Truncate(option.Description, 40)}[/]"
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Show option selection menu
            var choices = command.Options.Select(o => o.Name).ToList();
            choices.Add("---");
            choices.Add("Run command");
            choices.Add("Reset to defaults");
            choices.Add("Back");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select option to configure:[/]")
                    .PageSize(15)
                    .HighlightStyle(new Style(Color.Cyan1))
                    .AddChoices(choices));

            if (choice == "Back")
                return [];

            if (choice == "Reset to defaults")
            {
                foreach (var option in command.Options)
                    configuredValues[option.Name] = option.DefaultValue;
                continue;
            }

            if (choice == "Run command")
            {
                // Build command line arguments
                foreach (var option in command.Options)
                {
                    var value = configuredValues[option.Name];
                    if (value != null && !Equals(value, option.DefaultValue))
                    {
                        args.AddRange(BuildArgument(option, value));
                    }
                }
                return args;
            }

            if (choice == "---")
                continue;

            // Configure selected option
            var selectedOption = command.Options.First(o => o.Name == choice);
            var newValue = PromptForValue(selectedOption, configuredValues[selectedOption.Name]);
            configuredValues[selectedOption.Name] = newValue;
        }
    }

    private static object? PromptForValue(OptionInfo option, object? currentValue)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]{option.Name}[/]: {option.Description}");
        
        if (option.DefaultValue != null)
            AnsiConsole.MarkupLine($"[dim]Default: {option.DefaultValue}[/]");
        
        if (currentValue != null)
            AnsiConsole.MarkupLine($"[dim]Current: {currentValue}[/]");

        AnsiConsole.WriteLine();

        var propertyType = option.PropertyType;

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // Handle enums
        if (underlyingType.IsEnum)
        {
            var enumValues = Enum.GetNames(underlyingType).ToList();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[cyan]Select {option.Name}:[/]")
                    .HighlightStyle(new Style(Color.Cyan1))
                    .AddChoices(enumValues));
            return Enum.Parse(underlyingType, selected);
        }

        // Handle booleans
        if (underlyingType == typeof(bool))
        {
            return AnsiConsole.Confirm($"Enable {option.Name}?", (bool)(currentValue ?? false));
        }

        // Handle integers
        if (underlyingType == typeof(int))
        {
            var defaultInt = (int)(currentValue ?? option.DefaultValue ?? 0);
            return AnsiConsole.Prompt(
                new TextPrompt<int>($"[cyan]{option.Name}:[/]")
                    .DefaultValue(defaultInt)
                    .ValidationErrorMessage("[red]Please enter a valid number[/]"));
        }

        // Handle doubles
        if (underlyingType == typeof(double))
        {
            var defaultDouble = (double)(currentValue ?? option.DefaultValue ?? 0.0);
            return AnsiConsole.Prompt(
                new TextPrompt<double>($"[cyan]{option.Name}:[/]")
                    .DefaultValue(defaultDouble)
                    .ValidationErrorMessage("[red]Please enter a valid number[/]"));
        }

        // Handle strings
        var defaultStr = currentValue?.ToString() ?? option.DefaultValue?.ToString() ?? "";
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"[cyan]{option.Name}:[/]")
                .DefaultValue(defaultStr)
                .AllowEmpty());
    }

    private static IEnumerable<string> BuildArgument(OptionInfo option, object value)
    {
        // Parse template to get the option flag
        var template = option.Template;
        var parts = template.Split('|');
        var flag = parts.Length > 1 ? parts[1].Split(' ')[0] : parts[0].Split(' ')[0];

        // Handle boolean flags
        if (option.PropertyType == typeof(bool))
        {
            if ((bool)value)
                yield return flag;
            yield break;
        }

        yield return flag;
        yield return value.ToString()!;
    }

    private static int RunCommand(string[] args)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Running: decima {string.Join(" ", args)}[/]");
        AnsiConsole.WriteLine();

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("Decima");
            config.AddCommandsFromAssembly();
        });

        return app.Run(args);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }

    private record CommandInfo(
        string Name,
        string Description,
        Type CommandType,
        List<OptionInfo> Options)
    {
        public string DisplayName => $"{Name,-10} [dim]{Description}[/]";
    }

    private record OptionInfo(
        string Name,
        string Template,
        string Description,
        Type PropertyType,
        object? DefaultValue,
        bool IsRequired,
        PropertyInfo Property);
}
