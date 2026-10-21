# ParticleFX Studio engine plugins

Import effects from [ParticleFX Studio](https://particlefx.studio) straight into
Godot and Unity. Browse the free effect library (or your own effects), click
Import, and get a ready node or prefab: sliced frames, the animation at the
effect's frame rate and loop setting, an additive glow material where the
effect needs it, and the pivot at the effect's centre.

Free accounts can import free library effects within the daily export
allowance. Your own and agent-made effects, FX packs, higher resolution,
normal maps and packed sheets need a [plan](https://particlefx.studio/pricing).
Imports never use AI builds. Exported effects can be used in any project,
including paid commercial games.

## Godot 4.2+

1. Copy `addons/particlefx` into your project's `addons/` folder (or install
   it from the Godot Asset Library).
2. Enable it in Project > Project Settings > Plugins.
3. Open the ParticleFX dock, paste an API key from
   [particlefx.studio/billing](https://particlefx.studio/billing), and import.

"Import zip..." also works offline with a zip downloaded from the site.

## Unity 2021.3+

Window > Package Manager > + > Add package from git URL:

```
https://github.com/ParticleFX-Studio/particlefx-engine-plugins.git?path=/unity/com.particlefx.studio
```

Then open Window > ParticleFX Studio, paste an API key, and import. See
[the package README](unity/com.particlefx.studio/README.md) for details.

## Licence

MIT. See [LICENSE](LICENSE).
