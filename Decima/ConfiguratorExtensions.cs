namespace Decima;

using System.Reflection;

using Spectre.Console.Cli;
using Spectre.Console.Cli.Unsafe;

public static class ConfiguratorExtensions
{
    extension(IConfigurator config)
    {
        public void AddCommandsFromAssembly()
        {
            var commandTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && t.Name.EndsWith("Command")
                            && t.IsAssignableTo(typeof(ICommand)));

            foreach (var commandType in commandTypes)
            {
                var commandName = commandType.Name
                    .Replace("Command", string.Empty)
                    .ToLowerInvariant();

                config.SafetyOff().AddCommand(commandName, commandType);
            }
        }
    }
}
