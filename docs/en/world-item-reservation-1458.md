# Server reservation clocks and owner offers

World-item state and packet `22` have different reservation semantics in Terraria `1.4.5.8`. `WorldItem.ReserveFor` changes the reserved player and reservation age, then publishes its time argument. It does not assign that argument to the server's `timeToKeepReservation` field. The client handler for `22` does assign the received value to its own field.

| Operation | Retained server clock | Published hold |
|---|---|---|
| Ordinary `FindOwner` calls `ReserveFor` | Preserved, normally `0` | `15` |
| Explicit local-player spawn reservation | Preserved, normally `0` | `100` |
| Source join baseline | Unchanged | `0` |
| Positive server clock advances in `UpdateItem` | Decremented by one | No new owner packet |
| Forced owner search | Cleared before the remaining guards | Depends on the resulting transition |

`WorldItemSnapshot.TimeToKeepReservation` now represents the server field. The existing `IWorldItemStateCommitSink` receives two optional publication arguments: `reservationOffer` and `excludedSource`. They accompany a specific event and never become stored entity fields or a second countdown. Allocation previews retain `reservationOffer` with each operation, so direct and deferred adoption publish the same packets after adopting the same raw state. A snapshot read after publication contains the server clock, without the old offer.

The generic trusted `TryApplyOwner` API remains an explicit server-state writer. Vanilla owner selection and release use `TryApplySourceOwner` with an exact snapshot preimage: handle, generation, revision and current state must match. That operation preserves the server clock unless the caller requests the source forced clear, then publishes the independent offer. Client packet `22` still cannot write authoritative owner state.

## Discovery, release and join

Reservation age is separate from the hold field. The source server increments nonnegative age, searches for an unowned item's owner at age modulo `5` equal to `1`, and revisits an active owner at age modulo `300` equal to `0`. A requested change while the old owner is active sends one targeted `39`, sets age to `-1` and waits for acknowledgement. Forced emergency release clears a positive hold on both transfer endpoints, including an already unowned endpoint; that state change is adopted even when no new `39` or `22` is sent. Owner discovery runs before pending stacking transfers complete, so completing a transfer cannot admit an earlier owner search in the same tick.

An authenticated `39` acknowledgement first relinquishes the current owner. Forced search clears the server clock; ordinary search preserves it. If the result remains unowned, the fallback `22` carries hold `0` and excludes the releasing connection. A newly selected owner receives the ordinary `15` offer. Shimmer, all-player grab delay and pending transfers retain their source selection guards. Runtime requests additionally retain generation checks; vanilla wire `39` itself has no generation field.

The production join encoder publishes `21` with ownership mode `None` followed by `22` with hold `0`, matching the original index-only calls. It does not replay the creation mode or an earlier live reservation offer. Instanced leases and sentinel slot `400` remain separate: the sentinel's owner payload is already a transient wire offer, and no physical snapshot or server timer is created for it. The standalone generic codec still encodes explicitly supplied fields; it is not the production join policy.

## Creation ownership is a publication argument

`NewItemOwnership` is the argument of the initial item publication, not retained original `WorldItem` state. Source allocations keep that argument on the creation operation, so direct and deferred publication preserve the initial `21` mode while the committed active snapshot has `Ownership=None`. Trusted direct-drop and reserved-drop creation routes make the same distinction. Later existing-item `21`, partial pickup and stacking synchronization use `None`, matching original index-only `WorldItem.SyncItem`; the client residual is checked against that mode.

Clearing the creation argument preserves physical item state, actual owner, grab delays, server reservation clock, enemy delay and funded custody. A historical ownership hint introduced through a generic trusted state import is normalized on the next source `21` synchronization; that normalization grants no funding and does not relax the exact body, stack, generation or inventory checks.

A creation callback's `WorldItemSnapshot.Ownership` is deliberately the transient publication argument and can differ from the current store snapshot for the same handle and revision. A commit callback is therefore a publication projection, not an exact authoritative-state preimage. Consumers needing a current preimage must capture and validate the current store state rather than feed the callback snapshot back into a state-changing operation.

The source no-broadcast/instanced route is separate: dedicated `Item.NewItem(noBroadcast:true)` skips `ApplySpawnOwnership`. The captured raw grab player and delay remain `0` for all four modes, while modes `1/2` still receive the original enemy delay `100` before that branch. `MakeInstanced` sends `90` with mode `None`; it does not replay the unused creation argument.

The [creation-ownership verification record](../verification/world-item-creation-ownership-20261028.json) binds a new original corpus of 17 profiles and 62 steps, with two byte-identical captures, valid Stone inventory fillers, complete physical RNG, original packets and state at each send. Actual private `Player.PickupItem` transfers two Wire units into the only available stack space and synchronizes the remainder; its outer contact/ownership eligibility checks are outside this component capture. Actual passive combining uses an explicitly named headless `netMode=2`/`myPlayer=0` caller. The runtime trusted-contact regression uses its own admitted position/context and independently matches the source wire literal; complete caller preimages are not claimed identical. Existing original fixtures remain unchanged. This is bounded lifecycle evidence, not full pickup, scheduler, GUI, Linux NativeAOT or whole-game parity.

## Earlier reservation evidence

The developer-only reference probe retains original raw fields, ages and network bytes separately. Its captures use the unmodified Linux-edition server under Windows CoreCLR; they do not establish Linux NativeAOT or a GUI playthrough. Existing wiring and tool fixtures remain unchanged. See [the reference recipe](../../tools/reference/wiring1458/README.md) and [wiring installation](wiring-tool-installation-1458.md).

Verified 2026-10-27: Release build with warnings as errors; focused 11184/11184, fast 25439/25439, unfiltered full 1566852/1566852; no failures, runner errors or skips. The complete full XML covers all 5330 discovered methods. A fresh Windows NativeAOT executable passes all five production smoke paths, including the direct/deferred reservation replay.

The original corpus contains 33 profiles and 79 steps, with two identical captures and no errors. All 7 causal controls fail as intended (16 failed cases, no runner errors); exact restoration passes 39/39. See the [durable verification record](../verification/world-item-reservation-1458.json) for hashes, shared merge files and platform limits.

Merged-main integration, 2026-10-28: the latest main and wiring union passes Release WAE, 237 connected regressions and all five fresh Windows NativeAOT shipping smokes. This renewed merge-now checkpoint does not claim a new fast/full or original GUI acceptance; the interrupted earlier full is not a pass. See [merge evidence and scope](../verification/wiring-main-merge-20261028.json).
