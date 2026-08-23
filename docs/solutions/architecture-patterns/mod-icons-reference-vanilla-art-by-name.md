---
title: "A mod gets a real icon by naming vanilla's, not by shipping one"
date: 2026-08-22
last_updated: 2026-08-22
category: architecture-patterns
module: EcoServerMod
problem_type: architecture_decision
component: icons
severity: high
applies_when:
  - "Giving a new item, skill, book, scroll, research paper or component an icon"
  - "Reaching for a placeholder icon because real artwork is not ready"
  - "An icon does not appear in game and the PNG is obviously present and correctly named"
  - "Two different things in the mod draw the same picture"
  - "Deciding whether an icon needs a scene GameObject, a PNG, and a bundle rebuild"
tags: [eco-modding, icons, has-icon, asset-bundle, modkit, addressables, name-matching, silent-failure, placeholder-art, deprecated-api]
related_components: [EcoServerMod/AdvancedElectronics, Assets/Art/AdvancedElectronics, scripts]
---

# A mod gets a real icon by naming vanilla's, not by shipping one

## Context

Four tech-tree entries needed icons. The work that followed built a placeholder pipeline: an
icon table, generated flat-colour PNGs, a scene GameObject per entry, a bundle rebuild, a
deploy, a restart. It worked — eleven coloured squares rendered correctly in a running client.

It was also the wrong thing to build, and the reason is worth stating plainly because the
mistake is easy to repeat: **a flat-colour placeholder is worse than no icon at all.** When a
class has no icon, the client draws its own missing-icon sprite (`IconManager.cs:169-188` in the
Eco source checkout — an unknown name is cached to `GetMissingIcon(...)` and warned about once).
That default is a competent, purpose-drawn graphic. A magenta square is not better than it, and
unlike the default it costs an Editor session, a bundle rebuild, a deploy and a server restart
every time it changes.

Underneath that was a factual error nobody checked: **the assumption that a mod must ship an
icon to have one.** It does not. Vanilla's entire icon library is addressable by name from a
mod, with no asset, no bundle entry, and no scene object.

## Guidance

### The registry is flat, global, and already full

`IconManager` keeps one dictionary, `nameToIcons`, keyed by icon name
(`Client/Assets/UI/Scripts/Icons/IconManager.cs:26`). Vanilla fills it from Addressables at
connect time (`Client/Assets/Scripts/Mods/ModBundleManager.cs:697-703`), mod bundles add to the
same dictionary, and every lookup — inventory, Ecopedia, tech tree, tooltips, chat — goes
through it. There is no namespace separating vanilla icons from mod icons.

So any name vanilla registered is a name a mod can ask for.

### Ask for one with `[HasIcon("Name")]`

```csharp
public class HasIconAttribute : Attribute
{
    public string IconName; //Null means uses the regular display name not localized.
    public HasIconAttribute(string iconName = null) => this.IconName = iconName;
}
```
`Server/Eco.Core/Controller/AutoGenViews.cs:49-53`

and the resolution:

```csharp
string? GetIconName(Type type)
{
    if (type.TryGetAttribute<HasStaticIconAttribute>(true, out var attr)) return type.TryCallStatic<string>(attr.StaticFuncName!, type);
    var name = type.Attribute<HasIconAttribute>()?.IconName;
    if (name == null) name = type.Name;
    return name;
}
```
`Server/Eco.Core/Controller/ControllerMarshalerService.cs:412-418`

The bare `[HasIcon]` on `Item` carries a null name, so everything falls back to the class name —
which is why the default behaviour is "look for an icon called exactly what I am called". Pass a
string and that becomes the name the client looks up instead.

**This is not a trick; vanilla does it.** `PublicStorageComponent` and
`SelectionStorageComponent` both carry `[HasIcon("StorageComponent")]`; `FuelSupplyComponent`
and `PowerConsumptionComponent` both carry `[HasIcon("PowerComponent")]`. Two classes, one
picture, no second asset. `GameActions.cs:966` uses `[HasIcon(nameof(IconUtils.MiscIcons.Settlements))]`
to name an icon from the shared catalogue in `Server/Eco.Shared/Icons/IconUtils.cs`, which lists
the symbolic and miscellaneous icons (`Skills`, `Crafting`, `QuestionMark`, `EmptyIcon`, …) by
enum rather than by loose string.

`[HasStaticIcon("FuncName")]` is the computed variant — a static method taking the `Type` and
returning the name, for a family whose icon varies by tier or tag.

Two cautions. Attribute lookup inherits by default
(`Server/Eco.Shared/Utils/ReflectionUtils.cs:258`), so a subclass with no attribute of its own
inherits the parent's *explicit name* too, not just the fact of having an icon — give the
subclass its own `[HasIcon(...)]` when that is wrong. And `[NoIcon]` on a class blocks an
inherited `[HasIcon]` entirely.

### What is available, and how to list it

Every sprite baked into vanilla's atlas is registered under its rect name. The catalogue is the
atlas's `.meta` sidecar in the Eco source checkout — 4,059 rects as of 0.14:

```bash
grep -oE '^      name: [A-Za-z0-9_]+' \
    <eco-checkout>/Content/Art/UI/Icons/UI_Icons_Baked_0.png.meta \
    | sed 's/.*name: //' | sort -u
```

Read the **sprite-sheet rects**, not the `nameFileIdTable` further down the same file — that
table retains stale entries for sprites that no longer exist, `_FG` names among them.

**Sprite names can contain spaces, and those are the ones that matter.** A `[A-Za-z0-9_]+`
name pattern silently drops 27 rects — the whole generic set. Count what you parsed: **4059**,
not 4032. This single regex hid the correct answer through a planning cycle, a review, and a
shipped implementation; every search came back "vanilla has no generic skill book icon", which
was false.

`Content/Art/UI/Icons/IndividualIcons/` holds only ~130 loose source PNGs, a partial set of
newer items. Skill books, scrolls and research papers are not among them; they exist only inside
the baked atlas. That does not matter — the point is the *name*, not the file.

### Generic beats another specialty's art

Two kinds of candidate, and they are **not** equivalent:

- A **per-specialty** name (`ElectronicsSkillBook`) is that specialty's artwork. Pointing
  Advanced Electronics' book at it draws a picture belonging to a different skill. It is a
  better-looking placeholder and still a placeholder — arguably worse than a neutral one,
  because a player can misread it as the wrong item.
- A **generic** name is content-correct for anything of its kind, and vanilla ships one for
  every category a skill mod adds:

| Mod class | Vanilla icon | Kind |
|---|---|---|
| `AdvancedElectronicsSkillBook` | `Skill Book` | generic — the blue book, no emblem |
| `AdvancedElectronicsSkillScroll` | `Skill Scrolls` | generic — a rolled grey scroll |
| `AdvancedElectronicsAssemblyItem` | `Crafting Table` | generic craft station |
| `AdvancedElectronicsSkill` | `Skills` / `Skills_FG` | generic skills emblem; no per-specialty generic exists |
| `EngineeringResearchPaperPostModernItem` | **none — must be drawn** | see below |

The rest of the generic set, all space-named: `Skill Books`, `Basic Research`,
`Modern Research`, `Advanced Research`, `Crop Seed`, `Raw Food`, `Animal Skin`, `Liquid Fuel`,
`Burnable Fuel`, `Work Party`, `Work Orders`, `Bank Accounts`, `Civic Articles`,
`Election Processes`, `Asphalt Road`, `Scientist Specialty`.

`BatteryItem` and the three drones have no vanilla counterpart of either kind — the atlas holds
no `Battery*` or `*Drone*` rect, and no generic fits.

The research paper is a subtler case and the reason "nearest sibling" is not a safe default.
Research paper art is a **two-axis system**: the emblem carries the *family* (geology,
metallurgy, dendrology, engineering, culinary, agriculture) and the border furniture carries
the *tier* (basic, advanced, modern). Vanilla never drew a PostModern tier in any family, so
`EngineeringResearchPaperModernItem` is not "the same paper one tier down" — it is the Modern
paper, a different item the player also holds, and reusing it makes two items look identical.
The grammar and the brief for drawing the missing tier are in
`docs/guides/2026-08-research-paper-icon-spec.md`.

**The general rule this sharpens:** a name is only safe to reference when it is generic, or when
it names *the same thing*. A name that differs from your class along any axis the art encodes —
tier, rank, material, profession — is a placeholder, however close it looks in the file listing.

### What other skill mods do

Checked against the mods in `.references/Mods/` — `IntelligenceSkillMod`, `ArcaneKnowledge`,
`Mixology`, `AnimalHusbandry`, `Beekeeping`:

- **None of them uses `[HasIcon]`.** Not one occurrence across every `.cs` in the set.
- `IntelligenceSkillMod` adds `IntelligenceSkill : Skill` (no book, no scroll) and ships a
  189 KB `.unity3d` whose payload contains `IntelligenceSkill` — the legacy bundle route, with
  its own drawn art.
- None ships source art; only built bundles, so what they drew cannot be inspected.

So the field convention is the bundle route, and `[HasIcon]` naming appears to be unused by
mods despite being how vanilla itself shares icons between classes. That is an argument for
documenting it, not against using it.

### When the art really is new

A mod cannot add to the baked atlas or to Addressables, so genuinely custom art has exactly one
route: the asset bundle. The client picks it up from a scene GameObject named for the server
class, with a child named `Icon` holding images named `FullImage` and `Foreground`
(`ModBundleManager.cs:896-915`).

The method that does it is called **`RegisterDeprecatedIconFromObject`**, which is easy to
misread — and I did. What is deprecated there is the *delivery*: shipping icons inside a mod
bundle instead of through Addressables. The **authoring model is vanilla's own**, unchanged.

### How a vanilla item gets its icon, end to end

This is worth knowing because it is the same shape as the mod route, and it explains the naming
that otherwise looks arbitrary:

1. **An artist slices the art into a source sheet.** Research papers live in
   `Content/Art/UI/Icons/UI_Icons_05.png`; `Skill Book` and `Skill Scroll` in `UI_Icons.png`;
   tag icons in `UI_Icons_Tags.png`. There are 20-odd such sheets, 256 rects each.
2. **`Content/Art/Scenes/Icons.unity` holds one `ItemTemplate` GameObject per icon**, named
   exactly for the server class — `EngineeringResearchPaperModernItem`, `ElectronicsSkillBook` —
   or for a tag/group string — `Skill Book`, `Skill Books`, `Skill Scrolls`, `Crafting Table` —
   with the source sprite assigned to its `Foreground`.
3. **`UISpriteBaker` renders those templates into `UI_Icons_Baked_0.png`** and names each baked
   rect from the GameObject: `spriteRect.name = entry.item.name + (entry.isBakedForeground ? "_FG" : "")`
   (`Client/Assets/Editor/EcoTools/UI/UISpriteBaker.cs:654`).
4. At connect time those baked sprites register into `IconManager.nameToIcons` under exactly
   those names.
5. The server sends `IconName`; the client looks it up.

Step 3 is why the source sheet says `EngineeringResearchPaperModern` while the atlas says
`EngineeringResearchPaperModern**Item**`: the baked name comes from the **GameObject**, never
from the art file. Same rule the mod route follows, and the same rule that makes a renamed
child or a mistyped GameObject a silent failure on either side.

So the mod is not doing something legacy and odd. It is doing what `Icons.unity` does, and
delivering the result in the only container available to it.

Its mechanics, since they are still needed:

```
server class name  (or the string in [HasIcon])
  -> scene GameObject of that exact name, under the scene's "Items" root
       -> child "Icon"
            -> child "Foreground"
                 -> Image.m_Sprite -> {guid}
                      -> the .meta sidecar declaring that guid
                           -> the PNG beside it
```

The client finds those images **by GameObject name**, not through the `ItemTemplate` component's
serialized fields. Rename `Icon` or `Foreground` and every icon in the mod stops registering at
once, silently, with the Inspector still looking correct. **The PNG filename binds nothing** —
getting it right while the GameObject name is wrong is a missing icon that looks cosmetic, and
pointing two GameObjects at one sprite GUID is a *wrong* icon that looks like success.
`MiningDroneItem` shipped the survey drone's picture for three weeks that way, with the
name-match gate green throughout. `scripts/validate-icon-binding.sh` exists to catch exactly
that and is worth keeping regardless of which route an icon takes.

Size is 128 × 128 (`Client/Assets/Editor/EcoTools/UI/UISpriteBaker.cs:58`, and every relevant
atlas rect). `_FG` is the background-less suffix and needs no separate asset: with no
`FullImage`, registration publishes the foreground sprite under both the plain name and
`name_FG`.

### Three ways the client reports a missing icon

| Signal | Where | Covers | Fires |
|---|---|---|---|
| `Ecopedia: Missing following icons: …` | `Client/Assets/UI/Scripts/Ecopedia/EcopediaManager.cs:85-92` | **Ecopedia pages only** — a class with no `[Ecopedia]` attribute can never appear | Once, at login |
| `Cannot find icon with name "X"` | `IconManager.cs:177` | Any lookup | Lazily, on first draw, once per name per session |
| `Missing following icons:` (load-time) | `ModBundleManager.cs:796` | Every non-hidden item | Only with quality-assurance mode on |

The first is the only whole-set signal, and it only speaks for classes that have a page — a
page's icon name is its declaring type's name
(`Server/Eco.Gameplay/EcopediaRoot/EcopediaManager.cs:145`). Observed 2026-08-22: it named
`BatteryRecipe`, a `RecipeFamily` with a page and no icon, confirming it covers *any* type with
a page rather than items specifically.

The second is easy to mistake for silence, because it fires on first draw. A surface nobody
opened says nothing.

### The `?` in the skill tree is not a missing icon

An undiscovered specialty renders as `?` regardless of its icon — vanilla's Electronics,
Industry and Mechanics all show one on the same screen. The icon is visible in the tooltip
swatches and in the Tech Tree node. Do not chase it.

## Why This Matters

The placeholder pipeline cost a full session — an Editor grant, a bundle rebuild, a deploy and
a server restart — to reach a state visibly worse than doing nothing, while a one-line attribute
per class would have given six of the eleven entries the game's own artwork with no assets, no
bundle, and no restart.

The general shape: **before building a pipeline to produce an asset, check whether the platform
already has the asset and a way to name it.** Eco's icon registry is flat and global, which is
exactly the sort of detail that reads as an implementation accident and is in fact the whole
extension point.

## When to Apply

Giving a new entry an icon, in order:

1. **Look for a vanilla name first**, and prefer a *generic* one — grep the atlas meta's sprite
   rects with a pattern that admits spaces, or the whole set is invisible. A generic icon is
   content-correct; another specialty's is a prettier placeholder.
2. If one fits, add `[HasIcon("ThatName")]` to the class and stop. No asset, no scene object, no
   bundle rebuild — the change is a server assembly deploy.
3. Only when nothing fits, author real artwork at 128 × 128 and take the deprecated bundle route:
   icon-table row, `Finish All Item Icons`, save the scene, run
   `scripts/validate-icon-binding.sh` and `scripts/validate-name-match.sh` before building the
   bundle, then deploy bundle *and* assembly together.
4. Never ship a flat-colour placeholder. The client's own missing-icon sprite is better and free.

## Examples

**The whole change, for a skill book with a vanilla sibling:**

```csharp
[Serialized]
[Weight(1000)]
[LocDisplayName("Advanced Electronics Skill Book")]
[Ecopedia("Items", "Skill Books", createAsSubPage: true)]
[HasIcon("Skill Book")]                // vanilla's generic book; nothing ships
public partial class AdvancedElectronicsSkillBook : SkillBook<AdvancedElectronicsSkill, AdvancedElectronicsSkillScroll> {}
```

Note the space in the name. `"ElectronicsSkillBook"` would also render, and would be wrong —
that is the Electronics skill's book, not a neutral one.

**Vanilla's own precedent, two classes sharing one icon:**

```csharp
[Serialized, CreateComponentTabLoc("Storage"), HasIcon("StorageComponent")]  // PublicStorageComponent
[CreateComponentTabLoc("Storage"), HasIcon("StorageComponent")]              // SelectionStorageComponent
```

**What a wrong binding looks like when the bundle route is used**, from the gate:

```
MISMATCH: 'MiningDroneItem' draws Assets/Art/AdvancedElectronics/Sprites/Icons/SurveyDroneItem_icon.png
          but should draw Assets/Art/AdvancedElectronics/Sprites/Icons/MiningDroneItem_icon.png.
```

The scene cannot be read line by line to find this: Unity emits components **before** the
GameObjects that own them, and every item carries a second `Image` on its `Background` child, so
the object graph has to be rebuilt from `m_GameObject` / `m_Father`.

## Related

- `scripts/validate-icon-binding.sh` — the GUID-resolving gate for the bundle route
- `scripts/validate-name-match.sh` — the name gate, which cannot see a wrong binding
- `docs/guides/2026-08-research-paper-icon-spec.md` — the research-paper family/tier
  grammar, measured, and the brief for the missing PostModern tier
- `docs/guides/2026-08-eco-icon-atlas-guide.md` — cropping vanilla art for offline
  comparison; note that referencing by name makes extraction unnecessary for anything shippable
- `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md` — the plan whose placeholder premise
  this supersedes
- `CLAUDE.md` — names the wiki checkout, where `Icons.md` documents the icon format
