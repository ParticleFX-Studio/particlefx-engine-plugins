# ParticleFX Studio for Godot

Browse the [ParticleFX Studio](https://particlefx.studio) effect library and
your own effects inside the Godot editor, and import them with one click as a
ready scene: an `AnimatedSprite2D` with the sprite sheet sliced, the animation
at the effect's fps and loop setting, an additive material for glow effects,
nearest filtering for pixel art, and the pivot at the effect's centre.

Godot 4.2 or newer. MIT licensed.

## Install

1. Copy the `addons/particlefx` folder into your project's `addons/` folder
   (or install it from the Godot Asset Library).
2. Open **Project > Project Settings > Plugins** and enable **ParticleFX Studio**.
3. The **ParticleFX** dock appears on the right (top-left slot of the right side).

## Use

- **API key**: paste a key from
  [particlefx.studio/billing](https://particlefx.studio/billing) (the **Get an
  API key** button opens it) and press **Save**. The key is stored in your
  Editor Settings (`particlefx/api_key`), never in the project, so it is not
  committed to version control.
- The line under the key shows your plan and how many imports you have left
  today.
- **Library** tab: every free effect, with a search box and a category
  filter. Browsing needs no account.
- **My effects** tab: effects you saved in the studio or made with an agent
  (paid plans).
- **Import** on a card renders the effect on the server and unpacks it into
  the **Target folder** (default `res://particlefx/`), one subfolder per
  effect, for example `res://particlefx/fireball/`. The editor then imports
  the sheet and opens the scene.
- **Scale**, **Normal map** and **Packed** layout are available on paid plans.
- **Import zip...** imports a bundle you downloaded from the website. No
  account is needed for this.

### What an import creates

```
res://particlefx/fireball/
  fireball.png            sprite sheet
  fireball_n.png          normal map (when requested)
  fireball_frames.tres    SpriteFrames, one AtlasTexture per frame
  fireball.tscn           AnimatedSprite2D, autoplays
  particlefx.json         manifest (frame rects, fps, loop, pivot, blend, license)
```

Instance `fireball.tscn` wherever you need the effect. For one-shot effects
(loop off) connect `animation_finished` to `queue_free` if you want the node
removed when it ends.

Importing the same effect again overwrites the files and reloads the scene if
it is open.

### Access

- Browsing the library is free.
- Importing a free library effect at scale 1 needs a free account (an API
  key) and uses the studio's daily export allowance.
- Your own effects, FX pack effects, scale above 1, normal maps and packed
  layout need a paid plan. When something needs a plan the dock says so and
  shows a **See plans** button.
- Imports never use builds.

## Settings

| Editor Setting          | Default                          | Notes                                   |
| ----------------------- | -------------------------------- | --------------------------------------- |
| `particlefx/api_key`    | empty                            | Your key. Per user, not per project.     |
| `particlefx/api_base`   | `https://mcp.particlefx.studio`  | API address. Only change it for testing. |

The target folder is remembered per project (in `.godot/`).

## Troubleshooting

- **"Your API key was not accepted"**: the key was revoked or mistyped. Make a
  new one on the billing page and press Save.
- **"Too many requests"** or **"busy"**: wait a minute and try again.
- **The scene did not open**: the editor was still importing. Open the `.tscn`
  from the FileSystem dock (the add-on selects it for you).

## Privacy

The add-on only talks to the ParticleFX Studio API (`particlefx/api_base`) and
downloads poster images from particlefx.studio. It sends your API key, the
effect you import, the import options, the target folder path and an
`X-PFX-Client` header with the add-on and Godot versions. Nothing else from
your project is read or sent.
