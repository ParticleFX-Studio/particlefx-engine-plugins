// ParticleFX Studio for Unity. MIT licensed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ParticleFX.Studio.Editor.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParticleFX.Studio.Editor
{
    /// <summary>
    /// Turns a ParticleFX bundle (zip with a sprite sheet and particlefx.json)
    /// into Unity assets: a sliced sprite sheet, an AnimationClip, an
    /// AnimatorController, an additive material when needed, and a prefab.
    /// Re-importing the same effect updates the assets in place, keeping their
    /// GUIDs so existing references survive.
    /// </summary>
    internal static class PfxEffectImporter
    {
        public const string AdditiveShaderName = "ParticleFX/Sprite Additive";
        const string AdditiveShaderPath = "Packages/com.particlefx.studio/Runtime/Shaders/ParticleFXAdditive.shader";
        const string NormalMapTextureName = "_NormalMap";

        public sealed class Result
        {
            public GameObject Prefab;
            public string Folder;
            public EffectManifest Manifest;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>Imports a zip from disk ("Import zip...").</summary>
        public static Result ImportZipFile(string zipPath, string targetFolder)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(zipPath); }
            catch (Exception e) { throw new PfxBundleException("Could not read " + zipPath + ": " + e.Message); }
            return ImportZip(bytes, targetFolder);
        }

        /// <summary>Imports a zip downloaded from the API.</summary>
        public static Result ImportZip(byte[] zipBytes, string targetFolder)
        {
            var bundle = EffectBundle.FromZip(zipBytes);
            return Import(bundle, targetFolder);
        }

        public static Result Import(EffectBundle bundle, string targetFolder)
        {
            var root = PfxPaths.NormalizeAssetFolder(targetFolder);
            if (root == null) throw new PfxBundleException("The target folder must be inside Assets.");

            var m = bundle.Manifest;
            var result = new Result { Manifest = m };
            result.Warnings.AddRange(m.Warnings);
            if (Math.Max(bundle.ImageWidth, bundle.ImageHeight) > 4096)
                result.Warnings.Add("The sprite sheet is " + bundle.ImageWidth + "x" + bundle.ImageHeight
                    + ". Some older mobile GPUs only load textures up to 4096 pixels; check this effect on your target devices.");

            string slug = PfxPaths.SanitizeSlug(!string.IsNullOrEmpty(m.Slug) ? m.Slug : m.Base);
            string baseName = PfxPaths.SanitizeSlug(!string.IsNullOrEmpty(m.Base) ? m.Base : slug);
            string folder = root + "/" + slug;
            result.Folder = folder;

            try
            {
                EditorUtility.DisplayProgressBar("ParticleFX Studio", "Writing " + m.Title, 0.1f);
                EnsureFolder(folder);

                string sheetPath = folder + "/" + baseName + ".png";
                string normalPath = folder + "/" + baseName + "_n.png";
                WriteAsset(sheetPath, bundle.Image);
                WriteAsset(folder + "/" + EffectBundle.ManifestName, System.Text.Encoding.UTF8.GetBytes(bundle.ManifestJson));

                Texture2D normalTex = null;
                if (bundle.NormalMap != null)
                {
                    EditorUtility.DisplayProgressBar("ParticleFX Studio", "Importing the normal map", 0.2f);
                    WriteAsset(normalPath, bundle.NormalMap);
                    AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
                    ConfigureNormalMap(normalPath, m, bundle.ImageWidth, bundle.ImageHeight);
                    normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                }

                EditorUtility.DisplayProgressBar("ParticleFX Studio", "Slicing the sprite sheet", 0.4f);
                AssetDatabase.ImportAsset(folder + "/" + EffectBundle.ManifestName, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceUpdate);
                ConfigureSheet(sheetPath, bundle, normalTex);

                var sprites = LoadSprites(sheetPath, bundle.Layout);

                EditorUtility.DisplayProgressBar("ParticleFX Studio", "Building the animation", 0.7f);
                var clip = CreateOrUpdateClip(folder + "/" + baseName + ".anim", baseName, sprites, bundle.Layout, m);
                string stateFullPath;
                var controller = CreateOrUpdateController(folder + "/" + baseName + ".controller", clip, out stateFullPath);

                Material material = null;
                if (m.IsAdditive)
                {
                    material = CreateOrUpdateAdditiveMaterial(folder + "/" + baseName + "_additive.mat", baseName, result.Warnings);
                }

                EditorUtility.DisplayProgressBar("ParticleFX Studio", "Saving the prefab", 0.9f);
                result.Prefab = CreateOrUpdatePrefab(folder + "/" + baseName + ".prefab", m, sprites[bundle.Layout.FrameToSprite[0]],
                    controller, stateFullPath, material);

                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return result;
        }

        // ---- files and folders -------------------------------------------------

        static string ProjectRoot
        {
            get { return Directory.GetParent(Application.dataPath).FullName; }
        }

        static void WriteAsset(string assetPath, byte[] bytes)
        {
            var full = Path.Combine(ProjectRoot, assetPath);
            File.WriteAllBytes(full, bytes);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid)) throw new PfxBundleException("Could not create the folder " + next + ".");
                }
                current = next;
            }
        }

        // ---- textures ----------------------------------------------------------

        static void ApplyCommonTextureSettings(TextureImporter importer, EffectManifest m, int width, int height)
        {
            importer.mipmapEnabled = false;
            importer.filterMode = m.PixelArt ? FilterMode.Point : FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            // Unity's default max size (2048) would silently downscale big sheets and
            // break every sprite rect, so raise it to fit the sheet.
            importer.maxTextureSize = SpriteSheetLayout.MaxTextureSizeFor(width, height);
        }

        static void ConfigureNormalMap(string path, EffectManifest m, int width, int height)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new PfxBundleException("Unity did not import " + path + " as a texture.");
            importer.textureType = TextureImporterType.NormalMap;
            ApplyCommonTextureSettings(importer, m, width, height);
            importer.SaveAndReimport();
        }

        static void ConfigureSheet(string path, EffectBundle bundle, Texture2D normalTex)
        {
            var m = bundle.Manifest;
            var layout = bundle.Layout;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new PfxBundleException("Unity did not import " + path + " as a texture.");

            // Importer settings first: the sprite data provider reads the import
            // mode when it is created, so it must already be Multiple.
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = (float)m.PixelsPerUnit;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            ApplyCommonTextureSettings(importer, m, bundle.ImageWidth, bundle.ImageHeight);

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            // Full rect meshes: tight meshes clip faint glow and cost import time per frame.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new PfxBundleException("The 2D Sprite package could not edit " + path + ".");
            provider.InitSpriteEditorDataProvider();

            // Keep the spriteIDs of sprites that already exist (re-import), so
            // anything referencing them keeps working.
            var existingIds = new Dictionary<string, GUID>(StringComparer.Ordinal);
            foreach (var r in provider.GetSpriteRects())
                if (r != null && !string.IsNullOrEmpty(r.name) && !existingIds.ContainsKey(r.name)) existingIds[r.name] = r.spriteID;

            var rects = new SpriteRect[layout.Sprites.Count];
            for (int i = 0; i < rects.Length; i++)
            {
                var def = layout.Sprites[i];
                GUID id;
                if (!existingIds.TryGetValue(def.Name, out id) || id.Empty()) id = GUID.Generate();
                rects[i] = new SpriteRect
                {
                    name = def.Name,
                    spriteID = id,
                    rect = new Rect(def.X, def.Y, def.W, def.H),
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2((float)def.PivotX, (float)def.PivotY),
                    border = Vector4.zero,
                };
            }
            provider.SetSpriteRects(rects);

#if UNITY_2021_2_OR_NEWER
            // 2021.2+: sprite names map to file IDs through this provider; without
            // it new sprites can get unstable IDs (see the 2D Sprite manual).
            var nameIds = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameIds != null)
                nameIds.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
#endif

            var secondary = provider.GetDataProvider<ISecondaryTextureDataProvider>();
            if (secondary != null)
            {
                var textures = (secondary.textures ?? new SecondarySpriteTexture[0])
                    .Where(t => t.name != NormalMapTextureName).ToList();
                if (normalTex != null)
                    textures.Add(new SecondarySpriteTexture { name = NormalMapTextureName, texture = normalTex });
                secondary.textures = textures.ToArray();
            }

            provider.Apply();
            importer.SaveAndReimport();
        }

        static Sprite[] LoadSprites(string sheetPath, SpriteSheetLayout layout)
        {
            var byName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath))
            {
                var s = o as Sprite;
                if (s != null) byName[s.name] = s;
            }
            var sprites = new Sprite[layout.Sprites.Count];
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite s;
                if (!byName.TryGetValue(layout.Sprites[i].Name, out s) || s == null)
                    throw new PfxBundleException("Unity did not create the sprite " + layout.Sprites[i].Name + ". Check the Console for texture import errors.");
                sprites[i] = s;
            }
            return sprites;
        }

        // ---- animation ---------------------------------------------------------

        static AnimationClip CreateOrUpdateClip(string path, string name, Sprite[] sprites, SpriteSheetLayout layout, EffectManifest m)
        {
            var clip = new AnimationClip { name = name, frameRate = (float)m.Fps };
            int n = layout.FrameToSprite.Count;
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

            var keys = new List<ObjectReferenceKeyframe>(n + 1);
            for (int i = 0; i < n; i++)
            {
                keys.Add(new ObjectReferenceKeyframe
                {
                    time = (float)SpriteSheetLayout.FrameTime(i, m.Fps),
                    value = sprites[layout.FrameToSprite[i]],
                });
            }
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());

            // Each frame should show for 1/fps, the last one included, so the clip
            // lasts frames/fps. Unity normally extends sprite (PPtr) clips by one
            // frame past the last key; if this version does not, hold the last
            // sprite with one more key so the last frame is not cut short.
            float expected = (float)SpriteSheetLayout.ClipLength(m);
            if (clip.length < expected - 0.5f / (float)m.Fps)
            {
                keys.Add(new ObjectReferenceKeyframe { time = expected, value = keys[n - 1].value });
                AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = m.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                // Copy into the existing asset to keep its GUID.
                EditorUtility.CopySerialized(clip, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(clip);
                return existing;
            }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        static AnimatorController CreateOrUpdateController(string path, AnimationClip clip, out string stateFullPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
            if (controller == null) throw new PfxBundleException("Could not create the AnimatorController at " + path + ".");

            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
            var layer = controller.layers[0];
            var sm = layer.stateMachine;
            var state = sm.defaultState;
            if (state == null)
            {
                state = sm.AddState(clip.name);
                sm.defaultState = state;
            }
            state.motion = clip;
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
            stateFullPath = layer.name + "." + state.name;
            return controller;
        }

        static Material CreateOrUpdateAdditiveMaterial(string path, string baseName, List<string> warnings)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AdditiveShaderPath);
            if (shader == null) shader = Shader.Find(AdditiveShaderName);
            if (shader == null)
            {
                warnings.Add("The ParticleFX additive shader was not found, so the effect uses normal blending. Reinstall the package.");
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                if (mat.shader != shader) mat.shader = shader;
                EditorUtility.SetDirty(mat);
                return mat;
            }
            mat = new Material(shader) { name = baseName + "_additive" };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ---- prefab ------------------------------------------------------------

        static GameObject CreateOrUpdatePrefab(string path, EffectManifest m, Sprite firstSprite,
            RuntimeAnimatorController controller, string stateFullPath, Material additive)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            // Editing the existing prefab's contents keeps any components or
            // children the user added to it. A new prefab is built in a preview
            // scene so the user's open scene is never touched or dirtied.
            Scene preview = default(Scene);
            GameObject root;
            if (exists)
            {
                root = PrefabUtility.LoadPrefabContents(path);
            }
            else
            {
                preview = EditorSceneManager.NewPreviewScene();
                root = new GameObject(string.IsNullOrEmpty(m.Title) ? m.Base : m.Title);
                SceneManager.MoveGameObjectToScene(root, preview);
            }
            try
            {
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr == null) sr = root.AddComponent<SpriteRenderer>();
                sr.sprite = firstSprite;
                if (additive != null) sr.sharedMaterial = additive;

                var animator = root.GetComponent<Animator>();
                if (animator == null) animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                var fx = root.GetComponent<ParticleFXEffect>();
                bool newComponent = fx == null;
                if (newComponent) fx = root.AddComponent<ParticleFXEffect>();
                fx.EditorConfigure(m.Id, stateFullPath, m.Loop, (float)SpriteSheetLayout.ClipLength(m),
                    m.Frames.Count, (float)m.Fps, m.SourceUrl, newComponent);

                bool ok;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
                if (!ok || saved == null) throw new PfxBundleException("Could not save the prefab " + path + ".");
                return saved;
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(preview);
                }
            }
        }
    }
}
