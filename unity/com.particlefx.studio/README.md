# ParticleFX Studio for Unity

Browse the [ParticleFX Studio](https://particlefx.studio) effect library inside
the Unity editor and import any effect as a ready prefab: a sliced sprite
sheet, an Animator playing the effect at its own fps and loop setting, an
additive material for glow effects, and the pivot at the effect's centre.

Unity 2021.3 LTS or later (2022.3 LTS and Unity 6 included). Works with the
Built-in render pipeline and URP (Universal and 2D renderers).

## Install

Package Manager, from git:

1. Window > Package Manager > + > Add package from git URL...
2. Paste the repository URL with the package path, for example:
   `https://github.com/ParticleFX-Studio/particlefx-engine-plugins.git?path=/unity/com.particlefx.studio`
   (add `#<tag or branch>` at the end to pin a version).

Package Manager, from disk:

1. Download or clone the repository.
2. Window > Package Manager > + > Add package from disk...
3. Pick `plugins/unity/com.particlefx.studio/package.json`.

The package depends on `com.unity.2d.sprite` (the 2D Sprite package); the
Package Manager adds it if your project does not have it.

## Use

1. Open Window > ParticleFX Studio.
2. Browsing the library is free. To import, click "Get an API key", sign in
   on particlefx.studio, create a key on the billing page and paste it into
   the window. The key is stored in this computer's Unity preferences
   (EditorPrefs), never in your project, so it is not committed to version
   control.
3. Search or pick a category, then click Import on an effect.

The package writes `Assets/ParticleFX/<effect>/` (change the target folder at
the bottom of the window) and selects the new prefab:

| File | What it is |
| --- | --- |
| `<effect>.png` | The sprite sheet, sliced into one sprite per frame. Point filtering for pixel art, bilinear otherwise; no compression, no mipmaps. |
| `<effect>_n.png` | Normal map (paid option), attached to the sheet as the `_NormalMap` secondary texture for 2D lights. |
| `<effect>.anim` | The animation clip at the effect's fps; loops when the effect loops. |
| `<effect>.controller` | Animator Controller with the clip as its default state. |
| `<effect>_additive.mat` | Additive material, created when the effect glows (blend "add"). |
| `<effect>.prefab` | SpriteRenderer + Animator + ParticleFXEffect. |
| `particlefx.json` | The effect's manifest (frames, fps, pivot), kept for reference. |

Importing the same effect again updates these assets in place. References
to them (and components you added to the prefab) are kept.

### Offline: Import zip...

Zips downloaded from particlefx.studio for Unity (or Godot) can be imported
with the "Import zip..." button. No API key is needed for that.

### Plans

Free accounts can import free library effects at scale 1 with a grid sheet,
within the daily export allowance. Your own effects, FX pack effects, larger
scales, packed sheets and normal maps need a paid plan. When something needs
a plan, the window says so and offers a "See plans" button.

## Using effects in your game

Drop the prefab in a scene, or spawn it:

```csharp
using ParticleFX.Studio;
using UnityEngine;

public class CastFireball : MonoBehaviour
{
    public ParticleFXEffect fireballPrefab;

    public void Cast(Vector3 at)
    {
        var fx = Instantiate(fireballPrefab, at, Quaternion.identity);
        fx.Finished += e => Debug.Log("Fireball done");
    }
}
```

`ParticleFXEffect`:

- `Play()`, `Restart()`, `Pause()`, `Stop()`, `Speed`.
- `Finished` event and an `On Finished` UnityEvent, raised once when a
  one-shot effect plays its last frame. Looping effects never finish.
- `When Finished` for one-shots: Keep Last Frame, Hide, Deactivate (for
  pooling) or Destroy (the default for one-shots).
- `Play On Enable` restarts the effect each time the object is enabled.

The additive material uses the `ParticleFX/Sprite Additive` shader (unlit,
`Blend SrcAlpha One`, no depth write). The SpriteRenderer colour tints and
fades the effect; the material's Intensity brightens it.

## Settings

- API base URL: Advanced foldout in the window (EditorPrefs key
  `ParticleFXStudio.ApiBase`). Default `https://mcp.particlefx.studio`.
  Unity 2022.1 and later block plain `http://` URLs unless Project Settings >
  Player > Allow downloads over HTTP is enabled.
- Sorting: set the SpriteRenderer's sorting layer and order on the prefab.

## Troubleshooting

- "Your API key is missing, wrong or revoked": create a new key on
  https://particlefx.studio/billing and paste it again.
- Previews show titles only: the server did not send a PNG poster this Unity
  version can decode. Importing still works.
- Very large sheets: sheets over 4096 pixels import at full size (the package
  raises the texture's Max Size), but some older mobile GPUs cannot load
  them. Check on your target devices.

## License

MIT. See LICENSE.md. Effects you import carry their own license, shown in
`particlefx.json` (library effects are free to use in commercial games).
