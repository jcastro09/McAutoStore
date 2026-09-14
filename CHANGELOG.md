# Changelog

## 1.1.0
- New: ground pickup. Items lying nearby are pulled into containers automatically,
  but only when a container already holds that same item — otherwise the item stays
  on the ground. Configurable radius, scan interval and settle delay.
- Fixed: the ground scan and the container scan shared one collider buffer, so the
  second sweep overwrote the first mid-loop and items were skipped.
- Ownership of a dropped item is now claimed only after a matching container is found.
- All text is in English.

## 1.0.0
- First release. One key (default `.`) stores your inventory into nearby containers.
- Follows Valheim's ZDO ownership protocol: claim ownership, force the sync and reload
  the cache before writing. Without that step the item is wiped on the container's next
  CheckForChanges().
- Respects wards, private and group containers, and containers in use.
- Built and validated against Valheim 1.0.12 (network 40) and BepInEx 5.4.2350.
