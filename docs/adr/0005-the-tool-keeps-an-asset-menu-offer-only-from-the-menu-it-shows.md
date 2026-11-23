---
date: 2026-11-23
status: accepted
area: breakwater tool
---

# The tool keeps an asset menu offer only from the menu it shows

## Context

While the tool is active, the game offers it each prefab picked in the asset menu or in Find It, through `TrySetPrefab`, ahead of the vanilla tools; a prefab it turns down goes to its own tool, which leaves this one. The offer does not say who made it: a click on an asset, the toolbar opening a category on the asset it remembers, Find It's plain click and Find It's reopen all arrive alike. The ways weighed, each failing on one point:

- Keep every prop the tool can lay: Find It's plain click on a prop no longer leaves for the Object tool.
- Keep a stone already among the stones, wherever it comes from: opening another menu whose remembered asset is a chosen prop keeps the tool, where the player meant to leave for that menu.
- Keep an offer without making it the tool's prefab: the toolbar clears its menu on every frame the active tool gives no prefab.
- Patch `ToolbarUISystem` to learn who offers: the mod patches no game code.

## Decision

The tool keeps an offer only from the menu it shows, or, while it shows none, an offer of a stone already chosen, as `TakeOffer`'s docblock in `BreakwaterToolSystem.Stones.cs` details; every offer it keeps becomes its prefab. It keeps the toolbar's clicks and category changes and the Stones row's add tile working, lets Find It's plain click leave the tool, and needs no patch.

## Consequences

- The game's silence on who offers leaves three cases wrong, accepted as limits:
  - Find It reopened on a chosen prop of another menu, while a category is shown, leaves the tool. Find It skipping its reopen offer when the active tool already lists the prefab would lift it; whether to ask Find It for that is the user's call.
  - Entered by its key, the tool stays active through a toolbar menu opened on a chosen stone, and a click in that menu adds a layable asset to the stones.
  - Find It's plain click on an asset of another category of the menu shown does nothing: the tool takes it for that category's opening and shows the asset selected.
- Opening a menu that remembers no asset sets the default tool in `ToolbarUISystem.SelectAssetMenu`, before any offer reaches the tool, so the tool leaves.
