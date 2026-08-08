using AstroCraft.Core.Players;

namespace AstroCraft.Client.UI;

public sealed class GameHud
{
    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float Hunger { get; set; }
    public int SelectedHotbarIndex { get; set; }
    public float Fps { get; set; }
    public bool IsPaused { get; set; }
    public bool IsConnected { get; set; }
    public string StatusText { get; set; } = "Connecting...";

    public void UpdateFromPlayer(PlayerState player)
    {
        Health = player.Survival.Health;
        Oxygen = player.Survival.Oxygen;
        Hunger = player.Survival.Hunger;
        SelectedHotbarIndex = player.Inventory.SelectedHotbarIndex;
    }

    public string BuildTitle()
    {
        if (IsPaused)
        {
            return "AstroCraft | PAUSED";
        }

        return $"AstroCraft | HP {Health:0} O2 {Oxygen:0} Food {Hunger:0} | Slot {SelectedHotbarIndex + 1} | {Fps:0} FPS | {StatusText}";
    }
}
