# Fishing crate authority, Terraria 1.4.5.8

The existing player inventory, private funded quantities, shared gameplay random source and world-item allocation owner execute crate opening as one prepared transaction. A client inventory image cannot fund a crate. An opening intent identifies the exact connection occupation, source slot and expected inventory revision; a replay, stale revision or unfunded slot is refused before adopting rewards or random state.

`PlayerAuthority.TryOpenFishingCrate` uses all 26 official crate identities. Hardmode rewards follow the crate identity, not the current world's Hardmode flag. Wooden, iron and golden crates retain distinct retry loops. Biome crates retain their guaranteed item, common draws and trailing extras. Remix and the known Skyblock low-tiles condition come from world owners. Retry and prefix budgets refuse the whole prepared transaction on exhaustion.

Source `ItemSlot.TryOpenContainer` grants rewards before decrementing the crate. Each source `QuickSpawnItem(int)` rolls its natural prefix, executes existing `Player.GetItem` storage and allocates overflow before selecting the next reward. Consequently the last crate still occupies its slot when the first reward is placed. Coin conversion uses the existing storage and copper-provenance rules. Overflow uses reserved world items with source velocity draws and the existing replication/persistence path. Inventory, private quantities and random state are adopted before observers. An open Void Bag with a residual reward is refused while its complete storage destination is not represented by this transaction.

The vanilla UI has no crate-opening intent packet: it reports inventory and possible overflow after local reward selection. The existing inventory journal retains the whole source image, funded quantities and ordered `packet 5` reports until `packet 138`. One source-exact funded crate decrement and the exact independently computed reward image establish opening. Rewards reported before the crate debit are compared against that retained source image, so their untrusted display quantities never become another reward or payment. A lone decrement also represents a legal cursor move and cannot establish opening. The explicit host action can request the operation directly. Mixed bank operations, several openings in one coalesced batch and a client RNG result outside the represented source cursor remain refused; transparent compatibility with every local vanilla UI history is not claimed.

The portable original fixture contains 416 reward observations: all 26 identities, eight seeds and two Remix contexts, plus 52 actual `ItemSlot.TryOpenContainer` last-unit observations. Unchanged official `Player.OpenFishingCrate` and `QuickSpawnItem/GetItem` produced the inventory, prefix and complete RNG-state observations. A separate clean-rule comparison invokes the same original grant method between draws. Omitting the wooden coin draw produces 32 mismatches; the restored rules produce zero. These are configured source-method observations, with otherwise empty inventory and Skyblock low-tiles false, rather than a whole-client or complete-world acceptance claim. Runtime test and combined Release/Native/live results are recorded in agent memory.

A trusted host uses the public `WorldRuntime` command boundary. Observation does not reserve stock; opening refuses if another operation changed the captured inventory revision or connection occupation. The runtime must be running to drain these commands. Cancellation stops waiting; an already queued action can still execute, so cancellation must not be treated as proof of a refund.

```csharp
var observed = await world.CaptureFishingCrateAsync(connection, sourceSlot, cancellationToken);
if (observed is { } crate)
{
    bool accepted = await world.OpenFishingCrateAsync(crate, cancellationToken);
}
```
