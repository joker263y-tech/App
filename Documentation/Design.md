# World and design

Everything below is original to this project. The game is inspired by the broad
atmosphere of dark-fantasy fiction — a fragile civilisation, things older than
it, power that has to be earned — but no prose, dialogue, scene, character,
creature, place, name, ability or piece of lore is taken from any existing work.

## The premise

The night does not end. It has not ended for eleven years.

Fire is the only thing the dark will not cross, so what is left of humanity
gathers around the last of it and calls the arrangement a city. Beyond the
firelight, the world has been quietly replaced by something that keeps its
shapes — a thing called **the Umbra**, which does not attack so much as absorb.
Places it has finished with are hollow: the buildings are still there, the
bones are still there, and the meaning has been taken out.

You are a **Warden**: one of the last people who walks into the dark on purpose.
Wardens do not win. They go out, find out what is happening, and come back with
less than they left with.

## Factions

**The Last Ember** — the survivors around the fire. Not a kingdom; a rationing
arrangement with a wall. Their position is that the dark is survivable, and the
cost of admitting otherwise is that the wall stops being manned.

**The Veilwardens** — what happened to the Wardens who went too deep and kept
going. They still keep watch. They no longer remember what they are watching
*for*, so they treat everyone as intruders, including other Wardens.

**The Umbra** — not a faction so much as a pressure. It does not want anything;
it fills what is empty. Its agents are hollow, which is why they cannot be
reasoned with and why killing them looks less like victory and more like
cleaning.

## Regions

| Region | Character |
| --- | --- |
| **Last Ember Camp** | Firelight, walls, the smell of rationed smoke. The safe hub. |
| **The Grey Wilds** | Open ground under a sky with nothing in it. Enormous shapes move at the edge of visibility and do not come closer. |
| **The Hollowed Ruins** | A city the Umbra finished. Perfectly intact, entirely silent. It is possible to find a family's table still set. |
| **The Sunken Ward** | A Warden outpost that flooded. Lit from underneath by something still burning. |
| **The Umbral Sanctum** | Where the first Warden went, and stayed. The story's endpoint. |

Regions form a graph with gated access — some doors only open once the story has
moved, which is why the world model tracks travel and prerequisites rather than
just locations.

## Creatures

| Creature | Role | Character |
| --- | --- | --- |
| **Hollow Walker** | Fodder | Something that used to be a person, still wearing the shape. Slow, numerous, and it does not react to pain. |
| **Cinder Hound** | Pressure | Fast, and burning. Closes distance whether you want it to or not, and leaves you on fire. |
| **Veilwarden** | Elite | A Warden who kept watch too long. Armoured, deliberate, and it fights like it already knows what you will do. |
| **Ashen Sentinel** | Chapter boss | Enormous, patient, and built out of everything the Umbra has taken. It does not chase. It waits, and it does not need to hurry. |

Each creature has a distinct stat profile, resistances, a loot table, and its own
AI tuning — aggro range, vision cone, memory, disengagement behaviour. Higher
level placements scale their health and power, so a late-game region does not
reuse the opening area's numbers.

## The Warden's abilities

Five slots, each with a clear job, so combat is about choosing the right answer
rather than cycling cooldowns.

| # | Ability | Job |
| --- | --- | --- |
| 0 | **Ember Edge** | Fast, cheap melee. The default while you work out what is happening. |
| 1 | **Umbra Lance** | Ranged bolt. Scales off Shadow Power, not Attack, so it is a build decision. Marks what it hits. |
| 2 | **Ashstep** | Mobility. Deals no damage by design — it is for leaving. |
| 3 | **Sunder** | Wide, slow, hits up to four. Costs the caster a stagger on a whiff, so it is a commitment. |
| 4 | **Ward of Embers** | Defensive. Warded and Empowered for eight seconds, on a long cooldown. |

Two of these teach their own lesson without a tutorial: Umbra Lance splits the
damage stat so a player cannot simply stack Attack, and Sunder's self-stagger
means a missed heavy is punishable.

## Combat principles

- **Telegraphed, not fast.** Every attack has a wind-up. The counterplay window
  is a real window, and it exists because the ability defines it, not because an
  animation happened to be long enough.
- **Positional.** Attacks resolve in arcs and cones after movement, so stepping
  out of a swing is a legitimate answer.
- **Deliberate.** Commitment and recovery mean attacking is always a decision
  with a cost.
- **Legible.** Damage is mitigated by armour through a diminishing-returns
  curve, and the numbers on the HUD come from the same maths the simulation uses.

## Difficulty intent

Scaling down gracefully matters more than pushing a flagship. The design targets
a stable frame rate on mid-range hardware first, which is why the simulation runs
on a fixed timestep independent of frame rate, why the visual layer is a set of
primitives that real art can replace without touching any game rule, and why
enemy AI is a small state machine rather than a navigation mesh with a crowd
simulation behind it.
