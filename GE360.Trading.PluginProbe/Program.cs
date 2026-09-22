using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Util;

namespace GE360.Trading.PluginProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: GE360.Trading.PluginProbe <plugin-directory>");
            return 2;
        }

        var pluginDirectory = Path.GetFullPath(args[0]);

        if (!Directory.Exists(pluginDirectory))
        {
            Console.Error.WriteLine($"Plugin directory not found: {pluginDirectory}");
            return 3;
        }

        Config.Set("plugin-directory", pluginDirectory);

        try
        {
            var matches = Composer.Instance
                .GetExportedTypes<IDataQueueHandler>()
                .Where(type =>
                    string.Equals(
                        type.Name,
                        "AlpacaBrokerage",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        type.FullName,
                        "QuantConnect.Brokerages.Alpaca.AlpacaBrokerage",
                        StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                Console.Error.WriteLine(
                    $"Expected exactly one Alpaca IDataQueueHandler, found {matches.Length}.");
                return 4;
            }

            var type = matches[0];

            Console.WriteLine(
                $"GE360 plugin probe OK: {type.FullName} from {type.Assembly.GetName().Name}.");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"GE360 plugin probe failed: {exception.GetType().Name}: {exception.Message}");
            return 5;
        }
    }
}
