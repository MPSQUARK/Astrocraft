namespace AstroCraft.Core.Players;

public sealed class SurvivalState
{
    public float Health { get; set; } = GameConstants.MaxHealth;
    public float Hunger { get; set; } = GameConstants.MaxHunger;
    public float Oxygen { get; set; } = GameConstants.MaxOxygen;
    public bool IsDead { get; set; }

    public void ResetToSpawn()
    {
        Health = GameConstants.MaxHealth;
        Hunger = GameConstants.MaxHunger;
        Oxygen = GameConstants.MaxOxygen;
        IsDead = false;
    }

    public void ApplyDamage(float amount)
    {
        if (IsDead)
        {
            return;
        }

        Health = System.Math.Max(0f, Health - amount);
        if (Health <= 0f)
        {
            IsDead = true;
        }
    }
}
