using System;
using System.Reflection;

namespace McAutoStore
{
    /// <summary>
    /// Soft integration with slot mods. If shudnal's ExtraSlots (or Azu Extended
    /// Player Inventory) is present, items sitting in their equipment / quick / food /
    /// ammo / misc slots are never auto-stored. No hard dependency: if the mod is
    /// absent, IsInModSlot always returns false.
    /// </summary>
    internal static class SlotCompat
    {
        private static Func<ItemDrop.ItemData, bool> _isInSlot;

        internal static void Init()
        {
            _isInSlot = null;
            // ExtraSlots.API.IsItemInSlot(ItemDrop.ItemData)
            _isInSlot = Bind("ExtraSlots.API", "IsItemInSlot")
                     ?? Bind("AzuExtendedPlayerInventory.API", "IsSlotItem")
                     ?? Bind("AzuExtendedPlayerInventory.API", "IsAzuSlot");
            McAutoStorePlugin.Log.LogInfo("SlotCompat: extra-slot detection "
                + (_isInSlot != null ? "active" : "not found (no slot mod)"));
        }

        private static Func<ItemDrop.ItemData, bool> Bind(string typeName, string method)
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = asm.GetType(typeName, false);
                    if (t == null) continue;
                    MethodInfo m = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(ItemDrop.ItemData) }, null);
                    if (m == null || m.ReturnType != typeof(bool)) continue;
                    return (Func<ItemDrop.ItemData, bool>)
                        Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, bool>), m);
                }
            }
            catch (Exception e)
            {
                McAutoStorePlugin.Log.LogWarning("SlotCompat bind failed for " + typeName + "." + method + ": " + e.Message);
            }
            return null;
        }

        internal static bool IsInModSlot(ItemDrop.ItemData item)
        {
            if (_isInSlot == null || item == null) return false;
            try { return _isInSlot(item); }
            catch { return false; }
        }
    }
}
