---
title: "A mod gets a real icon by naming vanilla's, not by shipping one"
date: 2026-08-22
last_updated: 2026-09-05
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

**Path convention.** Every `Server/`, `Client/` and `Content/` path below is relative to the
**Eco source checkout**, a sibling of this repository — not to this repo, which contains none
of them. Repo-relative paths are written from `Assets/`, `EcoServerMod/`, `scripts/` or `docs/`.

### The registry is flat, global, and already full

`IconManager` keeps one dictionary, `nameToIcons`, keyed by icon name
(`Client/Assets/UI/Scripts/Icons/IconManager.cs:26`). Vanilla fills it from Addressables at
connect time (`Client/Assets/Scripts/Mods/ModBundleManager.cs:697-703`), mod bundles add to the
same dictionary, and every lookup — inventory, Ecopedia, tech tree, tooltips, chat — goes
through it. There is no namespace separating vanilla icons from mod icons.

So any name vanilla registered is a name a mod can ask for.

### Four fields, four consumers, and they are not interchangeable

There is no shared default. Set one and the icon changes on some surfaces and not others, which
reads as "the fix did not work" and costs a server restart per guess. It cost four.

| What you set | Where it lands | Who draws from it |
|---|---|---|
| `[HasStaticIcon("Method")]` | `ViewClassInfo.IconName` | Ecopedia pages |
| `[HasIcon("Name")]` | read at `TypeTooltips.cs:46` | type tooltips |
| `public override string IconName` | `Item.IconName`, synced per instance | inventory slots, hotbar, storage, recipe **rows** |
| `protected override ItemIconUILink` | `ItemLinkable.cs:56` | **inline icons in tooltip and chat text** |

The fourth is easy to miss because it looks like the third and is not:

```csharp
protected virtual LocString ItemIconUILink(LocString text) => TextLoc.Item(TextLoc.Icon(this.Name, text));
```
`Server/Eco.Gameplay/Items/ItemLinkable.cs:56`

Keyed on `Name`, not `IconName`. Override it to pass `this.IconName` so one string drives
everything rather than four literals drifting apart.

The third is the one that matters most and the one with no attribute at all:

```csharp
[SyncToView] public virtual string IconName    => this.Name;
```
`Server/Eco.Gameplay/Items/Item.cs:34`

An item instance's icon name is a plain virtual property defaulting to the class name. No
attribute touches it. Override it, and set the other two to the same string.

A further trap inside the attribute path: `[HasIcon("X")]` on an `Item` subclass appears not to
take, because `Item` itself carries a bare `[HasIcon]` whose `IconName` is null and the lookup
inherits. That is why vanilla only ever passes a name to `[HasIcon]` on components. Setting all
four fields sidesteps the question.

### Some surfaces have no override at all, and the class name is the last word

Two consumers cannot be redirected from the server classes:

- **A recipe's icon is its first product's class `Name`** —
  `TextLoc.Icon(this.DefaultRecipe.Products[0].Item.Name, …)` at
  `Server/Eco.Gameplay/Items/Recipes/RecipeFamily.cs:241`, mirrored in `Recipe.cs:138`. Both are
  plain, **non-virtual** methods on `ILinkable`. There is no override point.
- **The display-name alias** — `ModBundleManager` calls `SetSpriteAlias(ServerName, DisplayName)`,
  mapping the item's display name onto whatever the bundle registered under its class name.

So the class name has the final say wherever no override exists, and **the only lever is what the
bundle registers under it**. While the mod shipped its own art under the class name, every such
surface drew that art no matter what the server declared — which is why a run of source-side
fixes kept improving some surfaces and leaving others untouched.

The rule that falls out: **a mod that names vanilla's icon must ship nothing under its own class
names.** Removing the icon-table rows is not tidying up after the fix; it is part of the fix.
Removing them makes the unoverridable surfaces fall back to the client's own missing-icon sprite
rather than to a coloured square — better, but still not vanilla's art. Getting vanilla's picture
onto those surfaces requires shipping vanilla's art under the class name, which is a licensing
question rather than a technical one.

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

### Three kinds of answer, and only one of them is not a placeholder

"Placeholder" turned out to name two very different things, and conflating them wasted a cycle:

1. **Not a placeholder.** Either the game has exactly **one** picture for this kind of thing, or
   the mod renders its own model. Naming vanilla's single skill-book icon borrows no identity —
   every skill-book icon in the atlas is byte-identical, and so is every skill scroll, so the
   specialty is carried by the item's name and never by its picture. Verify before assuming:
   `md5sum` the extracted crops.

2. **A borrowed-sibling placeholder.** A related item's genuine art — right subject, right plate,
   and strictly better than the client's default — but it belongs to another item, and the two
   are indistinguishable until the mod has art of its own. `AdvancedElectronicsSkill` and
   `AdvancedElectronicsUpgradeItem` draw Electronics' emblem and module, because unlike books and
   scrolls those **do** differ per specialty. This ships, and it goes on the replacement list.

3. **A flat-colour placeholder.** Worse than shipping nothing: the client's own missing-icon
   sprite is a competent drawing that costs nothing, and a coloured square costs an Editor
   session, a bundle rebuild, a deploy and a restart every time it changes. Never.

The distinction that matters: (2) must be **replaced**, (3) should never have been **written**.

### Beware the space-named generics

They look like the obvious answer for a mod and they are usually wrong. `Skill Book`,
`Skill Scrolls`, `Skills` and `ModernUpgrade` are **tag icons** — the same drawing on a grey
plate, for category headers. Real items sit on olive or navy plates, so a tag icon in an
inventory slot has a visibly wrong background next to everything around it.

Check the plate before committing to a name: extract the candidate and a real item of the same
kind and put them side by side.

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
| `AdvancedElectronicsSkillBook` | `ElectronicsSkillBook` | **not a placeholder** — every skill book is one picture |
| `AdvancedElectronicsSkillScroll` | `ElectronicsSkillScroll` | **not a placeholder** — every skill scroll is one picture |
| `AdvancedElectronicsSkill` | `ElectronicsSkill` | borrowed sibling — skill emblems differ per specialty |
| `AdvancedElectronicsUpgradeItem` | `ElectronicsUpgradeItem` | borrowed sibling — upgrade modules differ per specialty |
| `EngineeringResearchPaperPostModernItem` | **none — must be drawn** | see below |

The rest of the generic set, all space-named: `Skill Books`, `Basic Research`,
`Modern Research`, `Advanced Research`, `Crop Seed`, `Raw Food`, `Animal Skin`, `Liquid Fuel`,
`Burnable Fuel`, `Work Party`, `Work Orders`, `Bank Accounts`, `Civic Articles`,
`Election Processes`, `Asphalt Road`, `Scientist Specialty`.

`BatteryItem`, `DroneDockItem` and the three drones have no vanilla counterpart of either kind —
the atlas holds no `Battery*` or `*Drone*` rect, and no generic fits. The drones did not need one:
see **Render it from the model** below.

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

- **None of them has any icon code at all** — no `[HasIcon]`, no `IconName`, no sprite handling,
  zero occurrences across every `.cs` in the set. The convention is to ship art in the bundle and
  let the class name resolve to it.
- `IntelligenceSkillMod` adds `IntelligenceSkill : Skill` (no book, no scroll) and ships a
  189 KB `.unity3d` whose payload contains `IntelligenceSkill` — the legacy bundle route, with
  its own drawn art.
- None ships source art, only built bundles — but a built bundle **can** be inspected, and was.
  `scripts/read-mod-bundle.py` decompresses a UnityFS container without Unity, and the four bundles
  it read tell a consistent story: each mod ships objects named after **its own** classes
  (`ArcaneKnowledgeSkillBook`, `AnimalHusbandryUpgradeItem`, `MixologySkillScroll`,
  `IntelligenceSkill`), **none** ships an object named after a vanilla asset, and none uses
  vanilla's `_FG` foreground-sprite convention. The check is by name only — a mod could still have
  traced vanilla art and shipped it under its own filename, which would need a Texture2D decoder to
  detect.

So the field convention is the bundle route, and `[HasIcon]` naming appears to be unused by
mods despite being how vanilla itself shares icons between classes. That is an argument for
documenting it, not against using it. It also means there is **no precedent among these mods for
redistributing vanilla's art**, which matters if the licensing question is ever put to Strange Loop
Games — see `docs/protocols/2026-08-31-slg-icon-licensing-ask.md`.

### Render it from the model

Before commissioning art, check whether the thing already exists in three dimensions, because
**that is how Eco makes its own icons**. `UISpriteBaker` composes a GameObject in a scene, points
a camera at it and screenshots it into the atlas — the RenderTexture at `UISpriteBaker.cs:505-520`
and the transparent-clear camera at `:583-587`. A mod can do the same thing into its own bundle:
`Assets/Art/AdvancedElectronics/Editor/AdvancedElectronicsIconRenderer.cs`.

That gave the three drones real icons with no artist. Four traps cost a render each:

1. **The materials do not render in the Editor.** The ModKit's `Curved/Standard` is a modified
   Unity Standard shader — Built-in Render Pipeline — and this project is HDRP, so every render
   came back Unity's magenta "no valid shader" colour. In game it never shows, because the Eco
   client supplies the pipeline those materials were written for. Substitute an unlit material
   carrying the original's albedo.
2. **Property names differ per pipeline.** `HDRP/Unlit` calls them `_UnlitColorMap` and
   `_UnlitColor`, not `_BaseColorMap`/`_BaseColor`. Setting names the shader does not have binds
   nothing, renders the shader's flat default, and reads as success. Try a list of names and
   report the shader's own property list when none matches.
3. **Bundled objects ship disabled.** The client keeps them as inactive templates, so a plain
   `InstantiatePrefab` photographs nothing. Force the instance and its children active.
4. **A shared model renders one picture under several names.** The three drones are one chassis —
   HRVSTR-01 doing different jobs — so they came back BYTE-IDENTICAL. `validate-icon-binding.sh`
   passes that correctly: each entry does point at its own file, and the files merely hold the
   same bytes. Fingerprint the outputs and fail on a collision; differentiate with a per-role
   tint applied as a modulation, not a replacement, so the albedo survives it.

**Render only what is worth photographing.** The dock and the assembly are hand-built primitives
wearing the placeholder material, so their renders were a flat diamond and a flat hexagon —
faithful to the model and worse than the client's own missing-icon sprite. The assembly took
`[HasIcon("Crafting Table")]` instead; the dock draws the default until its model is real.

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

### How to find the consumer instead of guessing at it

The failure mode that cost the most was reasoning from the surfaces that were already right.
Each fix worked, each looked like it had failed, and the next guess was aimed at another field.

What actually settled it, every time, was picking the **one surface that is wrong** and grepping
the game source for how *that* surface builds its icon — `TextLoc.Icon`, `SetIcon`, `IconName`,
`UILinkContent`. The consumers are all within two greps of each other, and each names its key
explicitly. Four fields were found that way; none was found by inference.

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
1b. **Otherwise check for a model worth photographing** and render it (see above). Free, accurate,
   and it is what vanilla does. Skip this when the model is itself a placeholder.
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

- `scripts/read-mod-bundle.py` — reads a built bundle's contents without Unity, which is how
  the mod survey above was done and how you check that a name reached your own bundle
- `docs/solutions/workflow-issues/a-timestamp-says-when-a-file-was-written-not-what-is-in-it.md`
  — the rule that instrument exists to serve, and the format traps it had to survive
- `scripts/validate-icon-binding.sh` — the GUID-resolving gate for the bundle route
- `scripts/validate-name-match.sh` — the name gate, which cannot see a wrong binding
- `docs/guides/2026-08-research-paper-icon-spec.md` — the research-paper family/tier
  grammar, measured, and the brief for the missing PostModern tier
- `docs/guides/2026-08-eco-icon-atlas-guide.md` — cropping vanilla art for offline
  comparison; note that referencing by name makes extraction unnecessary for anything shippable
- `docs/plans/2026-08-10-001-feat-tech-tree-icons-plan.md` — the plan whose placeholder premise
  this supersedes
- `CLAUDE.md` — names the wiki checkout, where `Icons.md` documents the icon format
