namespace AstroCraft.Core.Crafting;

public static class ToolBreakSpeed
{
    public const float HandMultiplier = 1f;
    public const float WoodMultiplier = 2f;
    public const float StoneMultiplier = 4f;
    public const float IronMultiplier = 6f;

    public static float GetMultiplier(ItemId itemId) => itemId switch
    {
        ItemId.WoodenPickaxe or ItemId.WoodenAxe or ItemId.WoodenShovel => WoodMultiplier,
        ItemId.StonePickaxe or ItemId.StoneAxe or ItemId.StoneShovel => StoneMultiplier,
        ItemId.IronPickaxe or ItemId.IronAxe or ItemId.IronShovel => IronMultiplier,
        _ => HandMultiplier,
    };

    public static bool IsTool(ItemId itemId) => GetMultiplier(itemId) > HandMultiplier;

    public static int GetMaxDurability(ItemId itemId) => itemId switch
    {
        ItemId.WoodenPickaxe or ItemId.WoodenAxe or ItemId.WoodenShovel => 60,
        ItemId.StonePickaxe or ItemId.StoneAxe or ItemId.StoneShovel => 132,
        ItemId.IronPickaxe or ItemId.IronAxe or ItemId.IronShovel => 251,
        _ => 0,
    };
}
