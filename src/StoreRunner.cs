using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace McAutoStore
{
    /// <summary>
    /// Moves items into nearby containers while respecting Valheim's ZDO ownership
    /// protocol. Writing into a Container's inventory without owning its ZDO makes the
    /// item vanish on the next CheckForChanges(), because Container.OnContainerChanged
    /// only calls Save() when IsOwner() is true.
    /// </summary>
    internal static class StoreRunner
    {
        private static readonly AccessTools.FieldRef<Container, ZNetView> ContainerNview =
            AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Container, uint> ContainerLastRevision =
            AccessTools.FieldRefAccess<Container, uint>("m_lastRevision");
        private static readonly AccessTools.FieldRef<Container, Piece> ContainerPiece =
            AccessTools.FieldRefAccess<Container, Piece>("m_piece");
        private static readonly MethodInfo ContainerLoad = AccessTools.Method(typeof(Container), "Load");

        private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> DropNview =
            AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");
        private static readonly MethodInfo DropTimeSinceSpawned = AccessTools.Method(typeof(ItemDrop), "GetTimeSinceSpawned");

        private static readonly Collider[] PieceBuffer = new Collider[512];
        private static readonly Collider[] ItemBuffer = new Collider[512];
        private static int _pieceMask = -1;
        private static int _itemMask = -1;

        // ---------------------------------------------------------------- inventory

        internal static void StoreInventory()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || player.InCutscene()) return;

            Inventory inv = player.GetInventory();
            if (inv == null) return;

            List<Container> containers = FindContainers(player.transform.position, McAutoStorePlugin.Range.Value);
            if (containers.Count == 0)
            {
                if (McAutoStorePlugin.ShowMessage.Value)
                    player.Message(MessageHud.MessageType.Center, "No container nearby");
                return;
            }

            HashSet<string> ignored = BuildIgnoreSet();
            int stored = 0;

            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (!Storable(player, item, ignored)) continue;
                foreach (Container c in containers)
                {
                    if (!TryStore(c, inv, item, McAutoStorePlugin.OnlyMatchingContainers.Value)) continue;
                    stored++;
                    break;
                }
            }

            if (McAutoStorePlugin.ShowMessage.Value)
                player.Message(MessageHud.MessageType.Center,
                    stored > 0 ? stored + " item(s) stored" : "Nothing to store");

            McAutoStorePlugin.Log.LogInfo("StoreInventory: " + stored + " item(s) into "
                                          + containers.Count + " container(s) in range.");
        }

        // ------------------------------------------------------------------- ground

        /// <summary>
        /// Pulls dropped items into nearby containers. An item is only pulled when some
        /// container already holds that same item; otherwise it is left on the ground.
        /// </summary>
        internal static void PullGroundItems()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || player.InCutscene()) return;

            Vector3 pos = player.transform.position;

            if (_itemMask < 0) _itemMask = LayerMask.GetMask("item");
            int n = Physics.OverlapSphereNonAlloc(pos, McAutoStorePlugin.GroundRange.Value, ItemBuffer, _itemMask);
            if (n == 0) return;

            List<Container> containers = null;
            int pulled = 0;
            var seen = new HashSet<ItemDrop>();

            for (int i = 0; i < n; i++)
            {
                ItemDrop drop = ItemBuffer[i].GetComponentInParent<ItemDrop>();
                if (drop == null || !seen.Add(drop)) continue;
                if (drop.m_itemData?.m_shared == null) continue;

                ZNetView nview = DropNview(drop);
                if (nview == null || !nview.IsValid()) continue;

                // respect the vanilla pickup delay and our own settle delay
                if (!drop.CanPickup()) continue;
                double age = (double)(DropTimeSinceSpawned?.Invoke(drop, null) ?? 0.0);
                if (age < McAutoStorePlugin.GroundMinAge.Value) continue;

                if (containers == null)
                {
                    containers = FindContainers(pos, McAutoStorePlugin.Range.Value);
                    if (containers.Count == 0) return;
                }

                foreach (Container c in containers)
                {
                    Inventory dest = c.GetInventory();
                    if (dest == null) continue;

                    // hard rule: the container must already hold this item
                    if (!dest.ContainsItemByName(drop.m_itemData.m_shared.m_name)) continue;
                    if (!dest.CanAddItem(drop.m_itemData)) continue;
                    if (!PrepareContainer(c)) continue;

                    // only claim the drop once we know it has somewhere to go;
                    // we must own it before destroying, or it respawns / duplicates
                    if (!nview.IsOwner())
                    {
                        nview.ClaimOwnership();
                        if (!nview.IsOwner()) break;
                    }

                    if (!dest.AddItem(drop.m_itemData)) continue;

                    nview.Destroy();
                    pulled++;
                    break;
                }
            }

            if (pulled > 0)
                McAutoStorePlugin.Log.LogInfo("PullGroundItems: " + pulled + " item(s) pulled from the ground.");
        }

        // ------------------------------------------------------------------ helpers

        private static HashSet<string> BuildIgnoreSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in (McAutoStorePlugin.IgnoredItems.Value ?? "").Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }

        private static bool Storable(Player player, ItemDrop.ItemData item, HashSet<string> ignored)
        {
            if (item?.m_shared == null) return false;
            if (McAutoStorePlugin.SkipEquipped.Value && player.IsItemEquiped(item)) return false;
            if (SlotCompat.IsInModSlot(item)) return false;   // ExtraSlots / AzuEPI extra slots
            if (McAutoStorePlugin.SkipHotbar.Value && item.m_gridPos.y == 0) return false;
            if (item.m_dropPrefab != null && ignored.Contains(item.m_dropPrefab.name)) return false;
            if (ignored.Contains(item.m_shared.m_name)) return false;
            return true;
        }

        /// <summary>Access checks plus ownership handshake. true when it is safe to write.</summary>
        private static bool PrepareContainer(Container c)
        {
            ZNetView nview = ContainerNview(c);
            if (nview == null || !nview.IsValid()) return false;
            if (c.GetInventory() == null) return false;

            bool inUse = nview.IsOwner() ? c.IsInUse() : nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
            if (inUse) return false;

            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;

            if (c.m_privacy == Container.PrivacySetting.Private)
            {
                Piece p = ContainerPiece(c);
                long myId = Game.instance.GetPlayerProfile().GetPlayerID();
                if (p == null || p.GetCreator() != myId) return false;
            }
            else if (c.m_privacy == Container.PrivacySetting.Group)
            {
                return false;
            }

            if (!nview.IsOwner())
            {
                nview.ClaimOwnership();
                if (!nview.IsOwner()) return false;
                ZDOMan.instance.ForceSendZDO(ZDOMan.GetSessionID(), nview.GetZDO().m_uid);
                ContainerLastRevision(c) = uint.MaxValue;   // invalidate the cached blob
                ContainerLoad?.Invoke(c, null);             // re-read the real ZDO state
            }
            return true;
        }

        private static bool TryStore(Container c, Inventory from, ItemDrop.ItemData item, bool requireMatch)
        {
            Inventory dest = c.GetInventory();
            if (dest == null) return false;
            if (requireMatch && !dest.ContainsItemByName(item.m_shared.m_name)) return false;
            if (!dest.CanAddItem(item)) return false;
            if (!PrepareContainer(c)) return false;
            if (!dest.AddItem(item)) return false;
            from.RemoveItem(item);
            return true;
        }

        private static List<Container> FindContainers(Vector3 pos, float range)
        {
            if (_pieceMask < 0) _pieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "vehicle");

            var seen = new HashSet<Container>();
            var list = new List<Container>();

            int n = Physics.OverlapSphereNonAlloc(pos, range, PieceBuffer, _pieceMask);
            for (int i = 0; i < n; i++)
            {
                Container c = PieceBuffer[i].GetComponentInParent<Container>();
                if (c == null || !seen.Add(c)) continue;
                list.Add(c);
            }

            list.Sort((a, b) =>
                (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
            return list;
        }
    }
}
