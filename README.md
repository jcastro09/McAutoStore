# McAutoStore

One key stores your inventory into nearby containers. Dropped items on the ground are
pulled in too — but only into a container that already holds that same item.

## What it does

**Store on keypress.** Press `.` (configurable) and every item in your inventory goes
into the nearest container that can take it.

**Ground pickup.** Items lying near you are moved into containers automatically. The
rule is strict on purpose: a container must **already hold that item**, otherwise the
item is left on the ground. Your loot never gets scattered into random chests.

## What it does NOT do

Stated up front so nobody installs it expecting something else:

- No hotkey to store a single hovered item
- No search command to find where an item ended up
- No favourite / lock system for slots
- No per-container YAML rules
- No ServerSync between server and clients

If you want that full feature set, [AzuAutoStore](https://thunderstore.io/c/valheim/p/Azumatt/AzuAutoStore/)
does all of it and is a mature mod.

## Configuration

`BepInEx/config/joaorodrigues.valheim.mcautostore.cfg`

| Option | Default | What it does |
|---|---|---|
| `StoreKey` | `.` | Key that stores your inventory |
| `Range` | `12` | Container search radius, in meters |
| `OnlyMatchingContainers` | `true` | Only store into a container that already holds that item. With `false`, any container with free space is used |
| `SkipHotbar` | `true` | Never store the first inventory row |
| `SkipEquipped` | `true` | Never store equipped items |
| `IgnoredItems` | `Hammer,Hoe,Cultivator,SledgeStagbreaker` | Prefab names that are never stored |
| `ShowMessage` | `true` | Show how many items were stored |
| `PullFromGround` | `true` | Pull dropped items into containers that already hold them |
| `GroundRange` | `12` | Radius scanned for dropped items, in meters |
| `GroundScanInterval` | `2` | Seconds between ground scans |
| `GroundMinAgeSeconds` | `3` | How long an item must lie on the ground first, so items you drop on purpose are not vacuumed instantly |

## Why it does not lose items

This is the hard part of any auto-store mod, and it is worth explaining.

A container's inventory is a local cache. The truth is a serialized blob inside the ZDO,
and `Container` only writes that blob when you own the ZDO:

```csharp
private void OnContainerChanged()
{
    if (!m_loading && IsOwner()) Save();
}
```

If a mod writes into a container's inventory without owning it, `Save()` never runs, the
item exists only in local memory, and the container's `CheckForChanges()` — which ticks
every second — reloads the blob over it. If the mod already removed the item from the
player, the item is gone.

McAutoStore follows the same protocol the game itself uses in `RPC_TakeAllResponse`:

1. `nview.ClaimOwnership()` — take ownership
2. `ZDOMan.instance.ForceSendZDO(...)` — force the latest blob to sync
3. invalidate `m_lastRevision` and call `Load()` — re-read the real state before writing
4. only then `AddItem`, and only remove from the player when `AddItem` returned `true`

Step 3 is what keeps you from wiping what another player stored seconds earlier.

For ground pickup the same care applies to the item itself: ownership of the dropped
item's ZDO is claimed **after** a matching container is found and **before** the drop is
destroyed, so an item is never destroyed without having landed in a container first.

## Multiplayer

Every player needs the mod. It works through ZDO ownership of the container, so it
behaves correctly with other players around, but installing it only on the server does
nothing.

## Respects

- Wards (`m_checkGuardStone` + `PrivateArea.CheckAccess`)
- Private containers (creator only) and group containers (never)
- Containers currently open by someone
- Ships and carts (`vehicle` layer)

## Compatibility

- Valheim 1.0.12 (network 40)
- BepInEx 5.4.2350

## License

MIT.
