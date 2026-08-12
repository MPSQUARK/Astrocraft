using AstroCraft.Core;
using AstroCraft.Client.Game;

if (args.Contains("--help"))
{
    PrintHelp();
    return;
}

string? address = GetArg(args, "--connect");
bool explicitConnect = address is not null;
if (address is null && !args.Contains("--discover"))
{
    address = "127.0.0.1";
}

int port = int.TryParse(GetArg(args, "--port"), out int parsedPort) ? parsedPort : GameConstants.DefaultGamePort;
string playerName = GetArg(args, "--name") ?? "Player";
bool discover = args.Contains("--discover");
bool flatWorld = args.Contains("--flat");
int criticSeconds = int.TryParse(GetArg(args, "--critic-seconds"), out int parsedCritic) ? parsedCritic : 0;
int criticMaxBootstrapSeconds = int.TryParse(GetArg(args, "--critic-max-bootstrap-seconds"), out int parsedBootstrap) ? parsedBootstrap : 90;
string? criticScreenshot = GetArg(args, "--critic-screenshot");
string? criticScreenshotDir = GetArg(args, "--critic-screenshot-dir");
string? criticFpsReport = GetArg(args, "--critic-fps-report");
bool showMainMenu = args.Contains("--menu");

if (address is null)
{
    address = "127.0.0.1";
}

ClientLaunchOptions options = new(address, port, playerName, discover, flatWorld, showMainMenu, criticSeconds, criticScreenshot, criticFpsReport, criticScreenshotDir, criticMaxBootstrapSeconds);
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
    Console.WriteLine("AstroCraft.Client [--menu] [--connect <ip>] --port <port> --name <player> [--discover] [--flat] [--critic-seconds <n>] [--critic-max-bootstrap-seconds <n>] [--critic-screenshot <path>] [--critic-screenshot-dir <dir>] [--critic-fps-report <path>]");
}
