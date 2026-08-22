---
title: "A mod icon binds by scene GameObject name, and a wrong one renders perfectly"
date: 2026-08-22
last_updated: 2026-08-22
category: architecture-patterns
module: Assets/Art/AdvancedElectronics
problem_type: architecture_decision
component: icons
severity: high
applies_when:
  - "Giving a new item, skill, book, scroll or research paper an icon"
  - "An icon does not appear in game and the PNG is obviously present and correctly named"
  - "Two different things in the mod draw the same picture"
  - "Deciding whether a class needs an icon attribute of its own"
  - "Looking for where Eco's icon specification actually lives"
tags: [eco-modding, icons, asset-bundle, modkit, name-matching, silent-failure, ecopedia, unity-scene, placeholder-art]
related_components: [Assets/Art/AdvancedElectronics, EcoServerMod/AdvancedElectronics, scripts]
---

# A mod icon binds by scene GameObject name, and a wrong one renders perfectly

## Context

Four tech-tree entries needed icons: `AdvancedElectronicsSkill`, its skill book, its skill
scroll, and `EngineeringResearchPaperPostModernItem`. Three had PNGs generated weeks earlier
and none had ever been checked in a running client; the skill had no icon asset at all.

Nobody could say what made an icon work. A repo task pointed at `Icons.md` without naming the
tree that holds it, and the file is not in this repository. The "templating" remembered as an
icon-authoring aid turned out to be the tech-tree spreadsheet transform, which emits C# class
declarations and no icon metadata at all.

Meanwhile `MiningDroneItem` had been shipping the survey drone's picture for three weeks. Both
scene objects referenced sprite GUID `b29fd48c15da025469d279d689ca1c52`, the mining drone had
no icon file of its own, `scripts/validate-name-match.sh` was green, and the result rendered
perfectly. That is the failure mode this whole area has: **a wrong icon looks exactly like
success.** A missing icon is not much better, because the base game ships missing icons too, so
it degrades quietly instead of failing.

## Guidance

### What binds, and what does not

The server asks for an icon **by class name**. `IHasIcon.IconName` resolves to the type's name,
and an absent icon attribute falls back to the type name rather than erroring
(`Server/Eco.Core/Controller/ControllerMarshalerService.cs:412-418` in the Eco source checkout).

The client answers with a sprite registered under that exact string. For a mod, that
registration walks the bundle **by GameObject name** — a child transform called `Icon`, then
`Image` components on GameObjects called `FullImage` and `Foreground`
(`Client/Assets/Scripts/Mods/ModBundleManager.cs:896-915`). It does **not** read the
`ItemTemplate` component's serialized fields. The ModKit's `ItemTemplate.prefab` happens to
supply that hierarchy, which is the only reason the mod's finisher works; rename the `Icon` or
`Foreground` child and every icon in the mod stops registering at once, silently, with the
Inspector still looking correct.

So the chain is:

```
server class name
  -> scene GameObject of that exact name, under the scene's "Items" root
       -> child "Icon"
            -> child "Foreground"
                 -> Image.m_Sprite -> {guid}
                      -> the .meta sidecar declaring that guid
                           -> the PNG beside it
```

**The PNG filename binds nothing.** `SurveyDroneItem_icon.png` is a human convenience. Getting
the filename right while the GameObject name is wrong produces a missing icon that looks
purely cosmetic; pointing two GameObjects at one GUID produces a wrong icon that looks like
nothing at all is broken.

Items are never saved as their own prefab files — per the ModKit's item flow they are unpacked
GameObjects living inside the scene. That is why the scene file, and not any asset file, is the
artifact carrying every icon's binding.

### A skill needs no attribute of its own

`Item` carries `[HasIcon]` (`Server/Eco.Gameplay/Items/Item.cs:28`), the attribute is read with
inheritance (`ControllerMarshalerService.cs:362-365`), and `Skill` derives from `Item`
(`Server/Eco.Gameplay/Skills/Skill.cs:34`). A modded `Skill` therefore resolves its icon exactly
the way an item does, needs no attribute, and needs the same name-matching GameObject under
`Items`. The wiki says the same thing in passing: the attribute "still should be on place (or on
a parent class)".

### Three ways the client tells you an icon is missing, and what each can see

| Signal | Where | What it covers | When it fires |
|---|---|---|---|
| `Ecopedia: Missing following icons: …` | `Client/Assets/UI/Scripts/Ecopedia/EcopediaManager.cs:85-90` | **Ecopedia pages only** — categories, pages, subpages. A class with no `[Ecopedia]` attribute can never appear. | Once, at login |
| `Cannot find icon with name "X"` | `Client/Assets/UI/Scripts/Icons/IconManager.cs:177` | Any lookup | Lazily, on first draw, once per name per session |
| Load-time enumeration of every icon-bearing class | `Client/Assets/Scripts/Mods/ModBundleManager.cs:795` | Everything | Only with quality-assurance mode enabled |

The first is the only one that speaks for a whole set in one log line, which makes it the
acceptance signal worth engineering for — but **only for classes that have a page**. A page's
icon name is its declaring type's name
(`Server/Eco.Gameplay/EcopediaRoot/EcopediaManager.cs:145`), which is how a class reaches that
report at all. `AdvancedElectronicsSkillScroll` had no `[Ecopedia]` attribute and was invisible
to it; giving it one is what made a single log read answer for all four.

Observed on 2026-08-22: that report named `BatteryRecipe`, a `RecipeFamily` with an Ecopedia
page and no icon of its own — so the mechanism really is "any type with a page", not "items".
**No skill of any kind has yet been observed in that report**, so whether it would name a skill
that lacked an icon is still unproven; ours has one and is correctly absent.

The second signal is easy to mistake for silence. It fires on first draw, so a surface nobody
opens says nothing. Open the skill-tree node, the Ecopedia pages, and a crafting or inventory
view before concluding an icon is fine.

### The "?" in the skill tree is not a missing icon

An undiscovered specialty renders as a `?` regardless of its icon — vanilla's Electronics,
Industry and Mechanics all show one on the same screen. The icon is visible in the tooltip's
inline swatches and in the Tech Tree node. Do not chase it.

### Size

128 × 128. Vanilla bakes its atlas at that size
(`Client/Assets/Editor/EcoTools/UI/UISpriteBaker.cs:58`), the wiki states it, and every skill,
book, scroll and research-paper rect in `Content/Art/UI/Icons/UI_Icons_Baked_0.png` is 128 × 128.
The mod's placeholders match so that size is not a variable when comparing against the real game.

### `_FG` costs nothing

`_FG` is the background-less suffix. Vanilla ships none for skills, books or scrolls, and both
variants for every research paper. It does not matter: when a mod object has no `FullImage`,
registration publishes the foreground sprite under **both** the plain name and `name_FG`
(`ModBundleManager.cs:896-915`). No `_FG` asset needs authoring.

Do not grep the atlas meta's `nameFileIdTable` to find out what vanilla ships — it retains stale
entries for sprites that no longer exist, `_FG` names among them. Read the sprite-sheet rects.

### Where the specification actually lives

`Icons.md` is in the **Eco wiki checkout**, a sibling of the Eco source checkout — not in this
repository, which is why a task citing it by bare filename read as citing something missing. It
is the authoritative format reference (128 × 128, the `_FG` suffix, the type name as the default
icon name, the attribute being inheritable).

It documents the **first-party Addressables and atlas-bake pipeline only**. The asset-bundle
path a mod uses appears nowhere in it. So it explains the format and says nothing about how a
mod's icon reaches the screen — that half is the table above, and it was read out of the client
source.

## Why This Matters

Every failure in this area is invisible or looks cosmetic:

- A **wrong** icon renders perfectly and is indistinguishable from success.
- A **missing** icon degrades to a placeholder the base game also shows.
- A **stale** bundle reports success at every step, because the bundle builder rejects only a
  never-saved scene — a scene saved once and dirty since builds silently from its on-disk copy.

Nothing about that set is caught by looking. It has to be caught by a check that resolves the
GUID, or by a log line that enumerates a whole set at once.

## When to Apply

Adding an icon to any new entry:

1. Add a row to `ItemIcons` in
   `Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsBuildTools.cs`, keyed by the exact
   server class name, with a fill in a hue region no other row occupies. **Measure it** — the
   closest pair in the table was once 20.7 dE, under the ~23 where two flat fills stop reading
   as different colours at thumbnail size.
2. If the class should be answerable by the enumerated report, give it an `[Ecopedia]` attribute.
3. Run `Eco Tools > Advanced Electronics > Finish All Item Icons`, or the
   `(Force Regenerate)` variant when an existing PNG must be rewritten. Force rewrites **in
   place** — deleting the PNG re-mints its GUID and dangles every scene reference to it.
4. Save the scene.
5. Run `scripts/validate-icon-binding.sh` and `scripts/validate-name-match.sh`. Both are headless
   and both run before the bundle is built, which is the last point where a mistake costs a
   re-run rather than a server restart.
6. Build the bundle, deploy the bundle **and** the server assembly, restart, and read the log.

## Examples

**The mis-binding, as the gate reports it:**

```
MISMATCH: 'MiningDroneItem' draws Assets/Art/AdvancedElectronics/Sprites/Icons/SurveyDroneItem_icon.png
          but should draw Assets/Art/AdvancedElectronics/Sprites/Icons/MiningDroneItem_icon.png.
```

The scene cannot be read line by line to find this. Unity emits components **before** the
GameObjects that own them, and every item carries a second `Image` on its `Background` child
holding a shared ModKit sprite, so neither line order nor proximity attributes a sprite to an
item. The object graph has to be rebuilt from `m_GameObject` / `m_Father`, and the shared
background excluded by its position in that graph rather than by its GUID.

**What a clean run looks like** (2026-08-22, Eco 0.14.0.3):

```
Ecopedia: Missing following icons: BatteryRecipe , FancyMediumLumberStoreWindowItem ,
  LargeGlassStoreWindowItem , LargeLumberStoreWindowItem , MediumLumberStoreWindowItem ,
  SmallLumberStoreWindowItem
```

None of the four tech-tree entries is named, and none of them appears anywhere in the log. The
skill drew magenta in the Tech Tree node, the book purple, the scroll parchment, the research
paper near-white, and the mining drone lime against the survey drone's blue.

**A caution from the same session:** near-white was chosen for the research paper because paper
is white. In the Laboratory's ingredient list it reads as an empty slot. Placeholder fills need
to be distinct from *each other* and from the UI they sit in; the second half was not considered.

## Related

- `scripts/validate-icon-binding.sh` — the GUID-resolving gate, and its own header comment
- `scripts/validate-name-match.sh` — the name gate, which cannot see this class of failure
- `docs/guides/2026-08-vanilla-icon-extraction-guide.md` — cropping vanilla art for comparison
- `docs/solutions/logic-errors/prefab-finisher-writes-to-the-scene-object-name.md` — the same
  name-versus-name confusion on the WorldObject side
- `CLAUDE.md` — names the wiki checkout as the icon specification's home
