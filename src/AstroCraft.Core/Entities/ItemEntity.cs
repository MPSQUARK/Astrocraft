using System.Numerics;

namespace AstroCraft.Core.Entities;

public sealed class ItemEntity
{
    public int Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
    public ItemStack Stack { get; } = new();
    public float SpinRadians { get; set; }
    public int PickupCooldownTicks { get; set; }
    public bool IsPredicted { get; set; }
}
