# Store listings

Everything needed to list the plugins. Submit from the **particlefxstudio**
GitHub account, after the repository is public.

## Godot Asset Library (godotengine.org/asset-library)

Sign in at https://godotengine.org/asset-library/asset/submit (the Asset
Library has its own account; create it with particlefxstudio@gmail.com).

| Field | Value |
| --- | --- |
| Asset name | ParticleFX Studio |
| Category | 2D Tools |
| Godot version | 4.2 |
| Version | 0.1.1 |
| Repository host | GitHub |
| Repository URL | https://github.com/ParticleFX-Studio/particlefx-engine-plugins |
| Issues URL | https://github.com/ParticleFX-Studio/particlefx-engine-plugins/issues |
| Download commit | the full hash of the v0.1.1 tag (`git rev-parse v0.1.1`) |
| Licence | MIT |
| Icon URL | https://raw.githubusercontent.com/ParticleFX-Studio/particlefx-engine-plugins/main/docs/images/icon-256.png |
| Preview 1 (image) | https://raw.githubusercontent.com/ParticleFX-Studio/particlefx-engine-plugins/main/docs/images/godot-editor.png |
| Preview 2 (image) | https://raw.githubusercontent.com/ParticleFX-Studio/particlefx-engine-plugins/main/docs/images/effects.gif |

Description (plain text, the Asset Library shows it as is):

```
Browse 650+ free game-ready 2D effects from ParticleFX Studio (particlefx.studio) inside the Godot editor and import any of them with one click.

Each import is a ready scene: an AnimatedSprite2D with the sprite sheet sliced, the animation at the effect's frame rate and loop setting, an additive material for glow effects, nearest filtering for pixel art, and the pivot at the effect's centre. Instance it anywhere.

- Library tab: search and filter every free effect (explosions, spells, hits, pickups, smoke and fire, pixel art, toon and anime, sci-fi, weather and more). Browsing needs no account.
- My effects tab: effects you made in the ParticleFX Studio editor or with its AI agent.
- Import zip: import a bundle downloaded from the website, no account needed.
- Paid plans add larger scales, normal maps for 2D lights and packed sheets.

Importing free effects needs a free account: paste an API key from particlefx.studio/billing into the dock. The key is stored in your Editor Settings, never in the project. Effects can be used in commercial games with no per-project licence.

After installing, enable it in Project > Project Settings > Plugins. The ParticleFX dock appears on the right.
```

The archive GitHub builds for the commit contains only `addons/particlefx`
(see `.gitattributes`), so installing never touches project.godot.

For each new version: bump `plugin.cfg` and `ADDON_VERSION` in
`api_client.gd`, tag, push, then edit the asset and paste the new commit hash.
Moderators review each submission and each edit (usually within a few days).

## Unity

The package installs from the git URL today:

```
https://github.com/ParticleFX-Studio/particlefx-engine-plugins.git?path=/unity/com.particlefx.studio
```

Pin a version with `#v0.1.1` at the end of the URL.

### OpenUPM (optional, free)

Lets people install with `openupm add com.particlefx.studio` and get update
notices in the Package Manager. Submit at https://openupm.com/packages/add/
with the repository URL; OpenUPM builds from git tags (`v0.1.1`), so every
release only needs a tag. It needs the package folder path
(`unity/com.particlefx.studio`), which the form asks for.

### Unity Asset Store (later)

Needs a publisher account, a store page with 1950x1300 and 1200x630 key
images, and review (a few weeks). Worth doing once the git URL version has
users. A free listing that links to particlefx.studio is allowed as long as
the package works without paying (browsing and free imports do).
