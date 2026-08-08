using AstroCraft.Core;
using AstroCraft.Server.Hosting;

if (args.Contains("--help"))
{
    PrintHelp();
    return;
}

string serverName = GetArg(args, "--name") ?? "AstroCraft Server";
int port = int.TryParse(GetArg(args, "--port"), out int parsedPort) ? parsedPort : GameConstants.DefaultGamePort;
int seed = int.TryParse(GetArg(args, "--seed"), out int parsedSeed) ? parsedSeed : 42;
bool flatWorld = args.Contains("--flat");

Console.WriteLine($"Starting {serverName} on port {port} (seed={seed}, flat={flatWorld})");
using GameServerHost host = new(serverName, port, seed, flatWorld);
host.Start();
Console.WriteLine("Server running. Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite);

static string? GetArg(string[] args, string key)
{
    int index = Array.IndexOf(args, key);
    if (index < 0 || index + 1 >= args.Length)
    {
        return null;
    }

    return args[index + 1];
}

static void PrintHelp()
{
    Console.WriteLine("AstroCraft.Server --name <name> --port <port> --seed <seed> [--flat]");
}
