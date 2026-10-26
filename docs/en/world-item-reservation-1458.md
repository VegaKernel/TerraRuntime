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

The later live `21` path remains a separate gap. Original `WorldItem.SyncItem` sends ownership mode `None` after a partial pickup or merge. Runtime partial/emergency transfers currently retain the initial snapshot mode (`ReserveForLocalPlayer = 1`, `GrabDelayForLocalPlayer = 2`), and the live encoder can repeat it; residual ingress also compares that retained mode. A vanilla residual with mode `None` can therefore be rejected for such a retained item. The present corpus verifies immediate creation and join, while its pending-merge scene starts with `None`; it does not close nonzero creation followed by a later `21`. The repeated low bits alone do not reset the original client's reservation clock.

The developer-only reference probe retains original raw fields, ages and network bytes separately. Its captures use the unmodified Linux-edition server under Windows CoreCLR; they do not establish Linux NativeAOT or a GUI playthrough. Existing wiring and tool fixtures remain unchanged. See [the reference recipe](../../tools/reference/wiring1458/README.md) and [wiring installation](wiring-tool-installation-1458.md).

Verified 2026-10-27: Release build with warnings as errors; focused 11184/11184, fast 25439/25439, unfiltered full 1566852/1566852; no failures, runner errors or skips. The complete full XML covers all 5330 discovered methods. A fresh Windows NativeAOT executable passes all five production smoke paths, including the direct/deferred reservation replay.

The original corpus contains 33 profiles and 79 steps, with two identical captures and no errors. All 7 causal controls fail as intended (16 failed cases, no runner errors); exact restoration passes 39/39. See the [durable verification record](../verification/world-item-reservation-1458.json) for hashes, shared merge files and platform limits.
