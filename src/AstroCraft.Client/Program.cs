using AstroCraft.Core;
using AstroCraft.Client.Game;

if (args.Contains("--help"))
{
    PrintHelp();
    return;
}

string? address = GetArg(args, "--connect");
if (address is null && !args.Contains("--discover"))
{
    address = "127.0.0.1";
}

int port = int.TryParse(GetArg(args, "--port"), out int parsedPort) ? parsedPort : GameConstants.DefaultGamePort;
string playerName = GetArg(args, "--name") ?? "Player";
bool discover = args.Contains("--discover");
bool flatWorld = args.Contains("--flat");

if (address is null)
{
    address = "127.0.0.1";
}

ClientLaunchOptions options = new(address, port, playerName, discover, flatWorld);
using AstroCraftGame game = new(options);
game.Run();

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
    Console.WriteLine("AstroCraft.Client --connect <ip> --port <port> --name <player> [--discover] [--flat]");
}
