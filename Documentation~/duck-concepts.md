---
doc_type: explanation
---

# Duck composition

## One or Many

- **One** holds a state that is on or off, such as a menu. Asserting it again changes nothing, and one release ends it
  for every holder.
- **Many** collects one claim per holder, such as two speakers. The music stays down until the last holder releases. A
  holder destroyed without releasing is released on the next frame.

## Composing

Every live duck contributes. The result is the most restrictive volume and the most restrictive of each filter cutoff,
so a dialogue duck and a menu duck together sound like both. A released duck leaves no contribution behind.

A lease identifies a hold. A release for an old lease leaves any new hold in place.

See [Ducks](ducks.md) to create, hold, and inspect a duck.
