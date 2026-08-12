using System.Numerics;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Entities;
using AstroCraft.Core.Players;

namespace AstroCraft.Core.Simulation;

public enum ItemPickupResult
{
    None = 0,
    PickedUp = 1,
    InventoryFull = 2,
}

public sealed class ItemPickupSystem
{
    public void UpdateMagnet(PlayerState player, ItemEntityWorld items, float deltaSeconds)
    {
        Vector3 pickupCenter = player.Position + new Vector3(0f, 0.5f, 0f);
        float magnetRadiusSq = GameConstants.ItemMagnetRadius * GameConstants.ItemMagnetRadius;
        float pickupRadiusSq = GameConstants.ItemPickupRadius * GameConstants.ItemPickupRadius;

        foreach (ItemEntity entity in items.Entities)
        {
            if (entity.PickupCooldownTicks > 0)
            {
                continue;
            }

            float distanceSq = Vector3.DistanceSquared(pickupCenter, entity.Position);
            if (distanceSq > magnetRadiusSq || distanceSq <= pickupRadiusSq)
            {
                continue;
            }

            float distance = MathF.Sqrt(distanceSq);
            if (distance < 0.001f)
            {
                continue;
            }

            Vector3 toPlayer = pickupCenter - entity.Position;
            Vector3 pull = toPlayer / distance * GameConstants.ItemMagnetPullSpeed * deltaSeconds;
            entity.Position += pull;
            entity.Velocity = pull / System.Math.Max(deltaSeconds, 0.001f);
        }
    }

    public ItemPickupResult TryPickup(PlayerState player, ItemEntityWorld items)
    {
        Vector3 pickupCenter = player.Position + new Vector3(0f, 0.5f, 0f);
        float radiusSq = GameConstants.ItemPickupRadius * GameConstants.ItemPickupRadius;
        ItemPickupResult result = ItemPickupResult.None;

        for (int i = items.Entities.Count - 1; i >= 0; i--)
        {
            ItemEntity entity = items.Entities[i];
            if (entity.PickupCooldownTicks > 0)
            {
                continue;
            }

            if (Vector3.DistanceSquared(pickupCenter, entity.Position) > radiusSq)
            {
                continue;
            }

            bool added = entity.Stack.ItemId != ItemId.None
                ? player.Inventory.TryAddItem(entity.Stack.ItemId, entity.Stack.Count)
                : player.Inventory.TryAddBlock(entity.Stack.BlockId, entity.Stack.Count);
            if (!added)
            {
                result |= ItemPickupResult.InventoryFull;
                continue;
            }

            items.Remove(entity.Id);
            result |= ItemPickupResult.PickedUp;
        }

        return result;
    }
}
