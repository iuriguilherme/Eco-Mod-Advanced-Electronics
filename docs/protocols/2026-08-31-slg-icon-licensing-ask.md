# Asking Strange Loop Games about icon art: what to ask, and what we already know

Four tech-tree entries currently draw vanilla's icon by naming it, and several surfaces still
draw nothing at all because they resolve by class name with no override point. Both of those
have the same possible fix — ship art under our own class names — and whether we may ship
*vanilla's* art that way is a licensing question, not a technical one.

This is the ask, written so it can be sent without re-deriving anything, plus the evidence
gathered before sending it.

## Why it is open at all

The mod is public, released, and licensed: **code LGPL-3.0-or-later, art CC BY-SA 4.0**
(`LICENSE`, `LICENSE-ART`). Redistributing Strange Loop Games' sprites under those terms would
be sublicensing their work, which we cannot do without permission.

There is also no document to read for the answer. **The ModKit ships with no licence file at
all** — no `LICENSE`, `EULA`, `TERMS` or copyright notice anywhere under `Assets/EcoModKit/`,
`Assets/EcoLibs/` or `Packages/com.strangeloopgames.eco-shared/`. It does ship four of the
game's own textures (the first-person arm maps under `Assets/EcoModKit/Assets/Food/HandsModel/`),
so art does travel to modders through the ModKit — with no stated terms attached. That absence
is the reason to ask rather than to infer.

## The two questions, which must not be merged

They have different answers and different consequences, and asking them together invites one
vague reply that settles neither.

### Q1 — may a mod redistribute the game's icon art?

> May a public, open-source Eco mod redistribute icon sprites taken from the game's own art —
> for items the mod itself adds — as part of its asset bundle? The mod is released under an
> open licence, so we need to know whether including your sprites is permitted, and if so under
> what attribution or notice.

A yes unlocks the cleanest technical outcome: ship vanilla's sprite under our class name, and
**every** name-keyed surface resolves correctly — including recipe rows and the display-name
alias, which have no override point in the server classes. All four source-side overrides could
then be deleted.

A no is equally useful. It closes the option permanently and makes the art brief the only path.

### Q2 — is there a supported method that needs no licence at all?

> Naming an existing icon works today: setting `IconName` to a vanilla sprite's name makes the
> client draw that sprite, and the mod ships no pixels. But some surfaces resolve by class name
> and are not overridable — `RecipeFamily.cs:241` and `Recipe.cs:138` both use
> `Products[0].Item.Name` from non-virtual methods, and `ModBundleManager.SetSpriteAlias` maps
> the display name onto whatever the bundle registered under the class name. Is there a
> supported way to point those surfaces at an existing icon without shipping art?

If such a hook exists and we simply did not find it, **Q1 becomes moot** — the mod would keep
shipping zero pixels and every surface would still be right. That is why Q2 is worth asking even
if Q1 gets a yes.

## Evidence gathered before asking

### The reference mods do not set a precedent for redistributing vanilla art

Four public mods that add skills were decompressed and inspected —
AnimalHusbandryReloaded, ArcaneKnowledge, IntelligenceSkillMod and Mixology — using
`scripts/read-mod-bundle.py`, which reads UnityFS containers without Unity.

| Finding | Result |
|---|---|
| Icon code in their C# | **None.** Zero occurrences of `[HasIcon]`, `IconName` or sprite handling across every `.cs` in the set |
| Objects named after their own classes | **Yes, all four** — `ArcaneKnowledgeSkillBook`, `AnimalHusbandryUpgradeItem`, `MixologySkillScroll`, `IntelligenceSkill` and so on |
| Objects named after *vanilla* assets | **None.** No `SkillBook`, `SkillScroll`, `Skills`, `ModernUpgrade` or `Crafting Table` in any bundle |
| The `_FG` foreground convention | **Not used by any of them** — zero `_FG` names across all four |

So the convention among these mods is to ship their own art under their own class names and let
the class name resolve to it. **No precedent was found for redistributing vanilla art**, which
means there is nothing to cite in the ask — and that is worth knowing before citing something
that turns out not to exist.

**What this does not establish.** The check is by name only. A mod could still have traced or
copied vanilla art and shipped it under its own filename; comparing pixels would need a
Texture2D decoder the reader deliberately does not have. The claim here is narrow: none of the
four ships an asset *named* after a vanilla one.

### What the game's own code already settles

- Four independent fields drive four different consumers, with no shared default. The map is in
  `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md`.
- Naming vanilla's icon works and ships nothing — the mod does it today for the skill, book,
  scroll and upgrade module.
- The unoverridable surfaces are the whole of the remaining problem. They are the only reason
  Q1 is worth asking.

## Where to ask

Not yet established, and worth settling before writing the message rather than after. The
candidates are Strange Loop Games' modding community channels and their public issue tracker;
`https://wiki.play.eco/en/Mod_Development` is the documentation entry point but is not itself a
venue. Pick a venue where the answer is **public and quotable** — a private reply helps this mod
and no one else, and the question is one every Eco mod eventually hits.

## What each answer changes

| Answer | Consequence |
|---|---|
| Q2 yes | Best case. Ship zero pixels, every surface correct, Q1 irrelevant, all four source-side overrides stay as they are |
| Q1 yes, Q2 no | Ship vanilla's sprites under our class names. Every surface correct. Delete the four overrides. Note the attribution SLG asks for |
| Both no | Current state is the end state for those four entries: the overridable surfaces draw vanilla's art by name, the unoverridable ones draw the client's missing-icon sprite until our own art exists. The art brief becomes the only path |

The third row is not a failure — it is what ships today, and it is already better than a
placeholder. Nothing here blocks the mod.

## Related

- `docs/briefs/2026-08-31-icon-art-brief.html` — the art request, which is the fallback in every
  branch above
- `docs/solutions/architecture-patterns/mod-icons-reference-vanilla-art-by-name.md` — the
  mechanism, and why the unoverridable surfaces are unoverridable
- `docs/protocols/2026-08-30-tech-tree-icons-handoff.md` — where this sits in the remaining work
