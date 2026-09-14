using System;
using System.Reflection;
using UnityEngine;

namespace McAutoStore
{
    /// <summary>
    /// Soft integration with slot mods. If shudnal's ExtraSlots (or Azu Extended
    /// Player Inventory) is present, items sitting in their equipment / quick / food /
    /// ammo / misc slots are never auto-stored. No hard dependency: if the mod is
    /// absent, IsInModSlot always returns false.
    ///
    /// Resolution is LAZY on purpose. BepInEx may load us before the slot mod - it does
    /// exactly that with ExtraSlots - so a scan of the loaded assemblies during Awake
    /// finds nothing and the integration silently stays off for the whole session.
    /// Binding on first use instead means the lookup happens long after every plugin
    /// has loaded, whatever the order was.
    /// </summary>
    internal static class SlotCompat
    {
        private static Func<ItemDrop.ItemData, bool> _isInSlot;
        private static bool _resolved;
        private static float _nextAttempt;

        private const float RetrySeconds = 2f;

        internal static void Init()
        {
            _isInSlot = null;
            _resolved = false;
            _nextAttempt = 0f;

            // One early attempt, for the common case where the slot mod loaded first.
            TryResolve(true);
            if (!_resolved)
                McAutoStorePlugin.Log.LogInfo(
                    "SlotCompat: no slot mod loaded yet, will look again on first use.");
        }

        private static void TryResolve(bool startup)
        {
            if (_resolved) return;
            if (!startup && Time.realtimeSinceStartup < _nextAttempt) return;
            _nextAttempt = Time.realtimeSinceStartup + RetrySeconds;

            Func<ItemDrop.ItemData, bool> found =
                   Bind("ExtraSlots.API", "IsItemInSlot")
                ?? Bind("AzuExtendedPlayerInventory.API", "IsSlotItem")
                ?? Bind("AzuExtendedPlayerInventory.API", "IsAzuSlot");

            if (found == null) return;

            _isInSlot = found;
            _resolved = true;
            McAutoStorePlugin.Log.LogInfo("SlotCompat: extra-slot detection active.");
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
            if (item == null) return false;
            if (!_resolved) TryResolve(false);
            if (_isInSlot == null) return false;
            try { return _isInSlot(item); }
            catch { return false; }
        }
    }
}
