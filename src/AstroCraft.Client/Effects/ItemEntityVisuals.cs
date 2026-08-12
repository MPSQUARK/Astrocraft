using System.Numerics;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Entities;

namespace AstroCraft.Client.Effects;

public static class ItemEntityVisuals
{
    private const float ItemIconSize = 0.22f;

    public static BlockParticle[] BuildParticles(IReadOnlyList<ItemEntity> entities, float timeSeconds)
    {
        BlockParticle[] particles = new BlockParticle[entities.Count];
        BlockRegistry registry = BlockRegistry.CreateDefault();

        for (int i = 0; i < entities.Count; i++)
        {
            ItemEntity entity = entities[i];
            byte textureIndex = entity.Stack.ItemId switch
            {
                ItemId.Coal => registry.Get(BlockId.CoalOre).TextureSide,
                _ => registry.Get(entity.Stack.BlockId).TextureSide,
            };
            float bob = MathF.Sin(timeSeconds * 4f + entity.Id * 0.73f) * 0.08f;
            particles[i] = new BlockParticle
            {
                Position = entity.Position + new Vector3(0f, bob, 0f),
                Velocity = Vector3.Zero,
                Life = 1f,
                MaxLife = 1f,
                Size = ItemIconSize,
                TextureIndex = textureIndex,
                SpinRadians = entity.SpinRadians,
            };
        }

        return particles;
    }
}
