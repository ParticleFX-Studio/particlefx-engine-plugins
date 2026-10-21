# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and the package uses
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-19

### Added

- Window > ParticleFX Studio: API key, account line, Library and My effects
  tabs, search, category filter, poster grid and Import buttons.
- Import options for paid plans: scale, packed sheet layout and normal maps.
- Import zip... for bundles downloaded from particlefx.studio.
- Importer: sliced sprite sheet (pivot at the effect centre, trimmed frames
  aligned), AnimationClip at the effect's fps and loop setting,
  AnimatorController, additive material for glow effects, `_NormalMap`
  secondary texture, and a prefab.
- `ParticleFXEffect` runtime component: Play, Restart, Pause, Stop, a
  Finished event, and what one-shots do when they finish (keep the last
  frame, hide, deactivate or destroy).
- `ParticleFX/Sprite Additive` shader for the Built-in pipeline and URP.
