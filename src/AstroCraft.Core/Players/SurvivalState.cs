namespace AstroCraft.Core.Players;

public sealed class SurvivalState
{
    public float Health { get; set; } = GameConstants.MaxHealth;
    public float Hunger { get; set; } = GameConstants.MaxHunger;
    public float Saturation { get; set; } = GameConstants.MaxSaturation;
    public double Exhaustion { get; set; }
    public float Oxygen { get; set; } = GameConstants.MaxOxygen;
    public bool IsDead { get; set; }
    public int RespawnTicksRemaining { get; set; }

    public void ResetToSpawn()
    {
        Health = GameConstants.MaxHealth;
        Hunger = GameConstants.MaxHunger;
        Saturation = GameConstants.MaxSaturation;
        Exhaustion = 0f;
        Oxygen = GameConstants.MaxOxygen;
        IsDead = false;
        RespawnTicksRemaining = 0;
    }

    public void AddExhaustion(float amount)
    {
        if (IsDead || amount <= 0f)
        {
            return;
        }

        Exhaustion += amount;
        while (Exhaustion >= GameConstants.HungerExhaustionThreshold)
        {
            Exhaustion -= GameConstants.HungerExhaustionThreshold;
            if (Saturation > 0f)
            {
                Saturation = System.Math.Max(0f, Saturation - GameConstants.SaturationLossPerExhaustionCycle);
            }
            else if (Hunger > 0f)
            {
                Hunger = System.Math.Max(0f, Hunger - GameConstants.HungerLossPerExhaustionCycle);
            }
        }
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
            RespawnTicksRemaining = GameConstants.RespawnDelayTicks;
        }
    }
}
