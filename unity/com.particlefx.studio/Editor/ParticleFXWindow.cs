// ParticleFX Studio for Unity. MIT licensed.
using System;
using System.Collections.Generic;
using ParticleFX.Studio.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace ParticleFX.Studio.Editor
{
    /// <summary>Window > ParticleFX Studio: browse the effect library and import effects as prefabs.</summary>
    public sealed class ParticleFXWindow : EditorWindow
    {
        const float CardWidth = 136f;
        const float PosterSize = 128f;
        const float CardHeight = 214f;
        const float CardSpacing = 8f;
        const double SearchDebounceSeconds = 0.35;

        static readonly string[] TabNames = { "Library", "My effects" };
        static readonly string[] ScaleNames = { "1x", "2x", "3x", "4x" };
        static readonly string[] LayoutNames = { "Grid", "Packed" };

        // Account
        string _keyField = "";
        bool _editingKey;
        PfxAccount _account;
        PfxApiError _accountError;
        bool _loadingAccount;

        // Lists
        int _tab;
        string _query = "";
        string _searchField = "";
        double _searchChangedAt = -1;
        string _category = "";
        readonly List<string> _categories = new List<string>();
        readonly List<PfxEffectItem> _items = new List<PfxEffectItem>();
        int _total;
        bool _loadingList;
        PfxApiError _listError;
        int _listSerial;
        Vector2 _scroll;
        Rect _scrollViewRect;

        // Import and messages
        string _importingId;
        string _status;
        MessageType _statusType = MessageType.Info;
        PfxApiError _error;
        bool _showAdvanced;
        string _apiBaseField;

        PfxThumbnails _thumbs;
        // Actions from GUI events run on the next editor update, so a click never
        // changes the layout halfway through an OnGUI pass.
        readonly List<Action> _deferred = new List<Action>();
        GUIStyle _cardTitle;
        GUIStyle _cardMeta;
        GUIStyle _posterText;

        [MenuItem("Window/ParticleFX Studio")]
        public static void Open()
        {
            var w = GetWindow<ParticleFXWindow>();
            w.titleContent = new GUIContent("ParticleFX Studio");
            w.minSize = new Vector2(340, 420);
            w.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("ParticleFX Studio");
            _thumbs = new PfxThumbnails(Repaint);
            _apiBaseField = PfxSettings.ApiBase;
            _editingKey = !PfxSettings.HasApiKey;
            EditorApplication.update += OnEditorUpdate;
            RefreshAccount();
            RefreshList(false);
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            PfxHttp.CancelAll(this);
            if (_thumbs != null) _thumbs.Dispose();
            _thumbs = null;
        }

        void Defer(Action action)
        {
            _deferred.Add(action);
        }

        void OnEditorUpdate()
        {
            if (_deferred.Count > 0)
            {
                var actions = _deferred.ToArray();
                _deferred.Clear();
                foreach (var a in actions)
                {
                    try { a(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                Repaint();
            }

            // Debounced search: query once typing pauses.
            if (_searchChangedAt >= 0 && EditorApplication.timeSinceStartup - _searchChangedAt >= SearchDebounceSeconds)
            {
                _searchChangedAt = -1;
                if (_searchField != _query)
                {
                    _query = _searchField;
                    RefreshList(false);
                }
            }
        }

        bool HasPlan { get { return _account != null && _account.HasPlan; } }

        // ---- requests ----------------------------------------------------------

        void RefreshAccount()
        {
            if (!PfxSettings.HasApiKey)
            {
                _account = null;
                _accountError = null;
                _loadingAccount = false;
                return;
            }
            _loadingAccount = true;
            PfxApiClient.Me(this, (account, error) =>
            {
                _loadingAccount = false;
                _account = account;
                _accountError = error;
                Repaint();
            });
        }

        void RefreshList(bool append)
        {
            int serial = ++_listSerial;
            if (!append)
            {
                _items.Clear();
                _total = 0;
                _scroll = Vector2.zero;
                if (_thumbs != null) _thumbs.ClearQueue();
            }
            _listError = null;

            if (_tab == 1 && !PfxSettings.HasApiKey)
            {
                _loadingList = false;
                return;
            }

            _loadingList = true;
            Action<PfxEffectPage, PfxApiError> done = (page, error) =>
            {
                if (serial != _listSerial) return; // a newer search replaced this one
                _loadingList = false;
                _listError = error;
                if (page != null)
                {
                    _total = page.Total;
                    _items.AddRange(page.Items);
                    if (_tab == 0 && page.Categories.Count > 0)
                    {
                        _categories.Clear();
                        _categories.AddRange(page.Categories);
                    }
                }
                Repaint();
            };
            if (_tab == 0) PfxApiClient.Library(this, _query, _category, append ? _items.Count : 0, done);
            else PfxApiClient.MyEffects(this, append ? _items.Count : 0, done);
        }

        void ImportEffect(PfxEffectItem item)
        {
            if (!PfxSettings.HasApiKey)
            {
                ShowError(new PfxApiError
                {
                    Status = 401,
                    Code = "invalid_token",
                    Description = "Importing needs an API key. Browsing is free; importing free effects needs a free account.",
                });
                return;
            }
            if (_importingId != null) return;
            _importingId = item.Id;
            ClearMessages();
            SetStatus("Rendering " + item.Title + " on the server...", MessageType.Info);

            bool plan = HasPlan;
            int scale = plan ? Mathf.Clamp(PfxSettings.Scale, 1, ScaleNames.Length) : 1;
            bool normal = plan && PfxSettings.NormalMap;
            bool packed = plan && PfxSettings.PackedLayout;

            PfxApiClient.Import(this, item.Id, scale, normal, packed, (zip, exportsLeft, error) =>
            {
                _importingId = null;
                if (error != null)
                {
                    _status = null;
                    ShowError(error);
                    if (error.IsAuth) _account = null;
                    Repaint();
                    return;
                }
                if (_account != null) _account.ApplyExportsLeftHeader(exportsLeft);
                ImportBundle(() => PfxEffectImporter.ImportZip(zip, PfxSettings.TargetFolder));
                Repaint();
            });
        }

        void ImportZipFromDisk()
        {
            var path = EditorUtility.OpenFilePanel("Import a ParticleFX Studio zip", "", "zip");
            if (string.IsNullOrEmpty(path)) return;
            ClearMessages();
            ImportBundle(() => PfxEffectImporter.ImportZipFile(path, PfxSettings.TargetFolder));
        }

        void ImportBundle(Func<PfxEffectImporter.Result> run)
        {
            try
            {
                var result = run();
                var msg = "Imported " + result.Manifest.Title + " to " + result.Folder + ".";
                if (result.Warnings.Count > 0) msg += "\n" + string.Join("\n", result.Warnings.ToArray());
                SetStatus(msg, result.Warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
                if (result.Prefab != null)
                {
                    Selection.activeObject = result.Prefab;
                    EditorGUIUtility.PingObject(result.Prefab);
                }
            }
            catch (PfxBundleException e)
            {
                SetStatus("Import failed: " + e.Message, MessageType.Error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus("Import failed: " + e.Message + " (details in the Console)", MessageType.Error);
            }
        }

        void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            _error = null;
        }

        void ShowError(PfxApiError error)
        {
            _error = error;
            _status = null;
        }

        void ClearMessages()
        {
            _error = null;
            _status = null;
        }

        // ---- GUI ---------------------------------------------------------------

        void EnsureStyles()
        {
            if (_cardTitle != null) return;
            _cardTitle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft };
            _cardMeta = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
            _posterText = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true, fontSize = 12 };
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawAccount();
            EditorGUILayout.Space(4);

            int tab = GUILayout.Toolbar(_tab, TabNames);
            if (tab != _tab)
            {
                Defer(() =>
                {
                    _tab = tab;
                    RefreshList(false);
                });
            }

            if (_tab == 0) DrawLibraryFilters();
            DrawList();
            DrawMessages();
            DrawOptions();
        }

        void DrawAccount()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_editingKey || !PfxSettings.HasApiKey)
                {
                    EditorGUILayout.LabelField("Browsing is free. Importing needs an API key from your ParticleFX Studio account.", EditorStyles.wordWrappedMiniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _keyField = EditorGUILayout.PasswordField("API key", _keyField);
                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_keyField)))
                        {
                            if (GUILayout.Button("Save", GUILayout.Width(50)))
                            {
                                var key = _keyField;
                                GUI.FocusControl(null);
                                Defer(() =>
                                {
                                    PfxSettings.ApiKey = key;
                                    _keyField = "";
                                    _editingKey = false;
                                    ClearMessages();
                                    RefreshAccount();
                                    RefreshList(false);
                                });
                            }
                        }
                        if (PfxSettings.HasApiKey && GUILayout.Button("Cancel", GUILayout.Width(60)))
                        {
                            Defer(() =>
                            {
                                _keyField = "";
                                _editingKey = false;
                            });
                        }
                    }
                    if (GUILayout.Button("Get an API key")) Application.OpenURL(PfxApiRoutes.KeysUrl);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    string line;
                    if (_loadingAccount) line = "Checking your account...";
                    else if (_account != null) line = _account.Summary;
                    else if (_accountError != null) line = _accountError.Description;
                    else line = "Account not loaded.";
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Refresh", GUILayout.Width(60))) Defer(RefreshAccount);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Change key")) Defer(() => { _editingKey = true; _keyField = ""; });
                    if (GUILayout.Button("Remove key"))
                    {
                        Defer(() =>
                        {
                            PfxSettings.ApiKey = null;
                            _account = null;
                            _accountError = null;
                            _editingKey = true;
                            RefreshList(false);
                        });
                    }
                    if (GUILayout.Button("Get an API key")) Application.OpenURL(PfxApiRoutes.KeysUrl);
                    if (_account != null && !_account.HasPlan && GUILayout.Button("See plans"))
                        Application.OpenURL(string.IsNullOrEmpty(_account.PricingUrl) ? PfxApiRoutes.PricingUrl : _account.PricingUrl);
                }
                if (_accountError != null && _accountError.IsAuth)
                    EditorGUILayout.HelpBox(_accountError.Description, MessageType.Warning);
            }
        }

        void DrawLibraryFilters()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                _searchField = EditorGUILayout.TextField(_searchField, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                if (EditorGUI.EndChangeCheck()) _searchChangedAt = EditorApplication.timeSinceStartup;

                var options = new string[_categories.Count + 1];
                options[0] = "All categories";
                for (int i = 0; i < _categories.Count; i++) options[i + 1] = _categories[i];
                int current = string.IsNullOrEmpty(_category) ? 0 : Math.Max(0, _categories.IndexOf(_category) + 1);
                int next = EditorGUILayout.Popup(current, options, EditorStyles.toolbarPopup, GUILayout.Width(140));
                if (next != current)
                {
                    var category = next == 0 ? "" : _categories[next - 1];
                    Defer(() =>
                    {
                        _category = category;
                        RefreshList(false);
                    });
                }
            }
        }

        void DrawList()
        {
            if (_tab == 1 && !PfxSettings.HasApiKey)
            {
                EditorGUILayout.HelpBox("Add your API key to see your own effects (saved in the studio or made by your agent). Your own effects need a paid plan.", MessageType.Info);
                GUILayout.FlexibleSpace();
                return;
            }

            if (_listError != null)
            {
                EditorGUILayout.HelpBox(_listError.Description, _listError.IsPaywall ? MessageType.Info : MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(_listError.UpgradeUrl) && GUILayout.Button("See plans")) Application.OpenURL(_listError.UpgradeUrl);
                    if (GUILayout.Button("Retry")) Defer(() => RefreshList(false));
                }
            }

            var countLine = _loadingList && _items.Count == 0 ? "Loading..."
                : (_items.Count == 0 && _listError == null ? "No effects found." : _total + (_total == 1 ? " effect" : " effects"));
            EditorGUILayout.LabelField(countLine, EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            float width = Mathf.Max(CardWidth, position.width - 24f);
            int columns = Mathf.Max(1, Mathf.FloorToInt((width + CardSpacing) / (CardWidth + CardSpacing)));
            int rows = (_items.Count + columns - 1) / columns;
            float viewTop = _scroll.y;
            float viewBottom = _scroll.y + Mathf.Max(200f, _scrollViewRect.height);

            for (int r = 0; r < rows; r++)
            {
                var rowRect = GUILayoutUtility.GetRect(width, CardHeight + CardSpacing);
                bool visible = rowRect.yMax >= viewTop - CardHeight && rowRect.y <= viewBottom + CardHeight;
                for (int c = 0; c < columns; c++)
                {
                    int index = r * columns + c;
                    if (index >= _items.Count) break;
                    var cell = new Rect(rowRect.x + c * (CardWidth + CardSpacing), rowRect.y, CardWidth, CardHeight);
                    DrawCard(cell, _items[index], visible);
                }
            }

            if (_items.Count > 0 && _items.Count < _total)
            {
                using (new EditorGUI.DisabledScope(_loadingList))
                {
                    if (GUILayout.Button(_loadingList ? "Loading..." : "Load more")) Defer(() => RefreshList(true));
                }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) _scrollViewRect = GUILayoutUtility.GetLastRect();
        }

        void DrawCard(Rect cell, PfxEffectItem item, bool visible)
        {
            GUI.Box(cell, GUIContent.none, EditorStyles.helpBox);
            var poster = new Rect(cell.x + (cell.width - PosterSize) / 2f, cell.y + 4, PosterSize, PosterSize);
            var url = item.BestPosterUrl;
            Texture2D tex = null;
            if (visible && _thumbs != null && url != null) tex = _thumbs.Get(url);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(poster, new Color(0.08f, 0.08f, 0.1f, 1f));
                if (tex != null) GUI.DrawTexture(poster, tex, ScaleMode.ScaleToFit, true);
            }
            if (tex == null)
            {
                // No decodable poster: show the title in the poster area.
                bool loading = url != null && _thumbs != null && !_thumbs.Failed(url);
                GUI.Label(poster, loading ? "Loading preview..." : item.Title, _posterText);
            }

            float y = poster.yMax + 4;
            var titleRect = new Rect(cell.x + 4, y, cell.width - 8, 32);
            GUI.Label(titleRect, new GUIContent(item.Title, item.Description), _cardTitle);
            y += 32;

            var meta = new List<string>();
            if (item.Own) { if (!string.IsNullOrEmpty(item.UpdatedAt) && item.UpdatedAt.Length >= 10) meta.Add(item.UpdatedAt.Substring(0, 10)); }
            else
            {
                meta.Add(item.Loop ? "Loop" : "One-shot");
                if (item.PixelArt) meta.Add("Pixel art");
                if (item.Premium) meta.Add("FX pack");
            }
            GUI.Label(new Rect(cell.x + 4, y, cell.width - 8, 16), string.Join(", ", meta.ToArray()), _cardMeta);
            y += 18;

            var buttonRect = new Rect(cell.x + 4, cell.yMax - 24, cell.width - 8, 20);
            bool busy = _importingId != null;
            using (new EditorGUI.DisabledScope(busy))
            {
                string label = _importingId == item.Id ? "Importing..." : "Import";
                if (GUI.Button(buttonRect, label)) Defer(() => ImportEffect(item));
            }

            // Click the poster to open the effect page on the website.
            if (!string.IsNullOrEmpty(item.Page))
            {
                EditorGUIUtility.AddCursorRect(poster, MouseCursor.Link);
                if (Event.current.type == EventType.MouseUp && Event.current.button == 0 && poster.Contains(Event.current.mousePosition))
                {
                    Application.OpenURL(item.Page);
                    Event.current.Use();
                }
            }
        }

        void SetApiBase(string apiBase)
        {
            PfxSettings.ApiBase = apiBase;
            _apiBaseField = PfxSettings.ApiBase;
            GUI.FocusControl(null);
            RefreshAccount();
            RefreshList(false);
        }

        void BrowseTargetFolder()
        {
            var abs = EditorUtility.OpenFolderPanel("Folder for imported effects", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs)) return;
            var assetFolder = PfxPaths.AbsoluteToAssetFolder(abs, Application.dataPath);
            if (assetFolder != null) PfxSettings.TargetFolder = assetFolder;
            else SetStatus("Pick a folder inside this project's Assets folder.", MessageType.Warning);
        }

        void DrawMessages()
        {
            if (_error != null)
            {
                EditorGUILayout.HelpBox(_error.Description, _error.IsPaywall ? MessageType.Info : MessageType.Error);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(_error.UpgradeUrl) && GUILayout.Button("See plans")) Application.OpenURL(_error.UpgradeUrl);
                    if (_error.IsAuth && GUILayout.Button("Get an API key")) Application.OpenURL(PfxApiRoutes.KeysUrl);
                    if (GUILayout.Button("Dismiss", GUILayout.Width(70))) Defer(() => _error = null);
                }
            }
            else if (!string.IsNullOrEmpty(_status))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(_status, _statusType);
                    if (_importingId == null && GUILayout.Button("Dismiss", GUILayout.Width(70), GUILayout.ExpandHeight(false))) Defer(() => _status = null);
                }
            }
        }

        void DrawOptions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var folder = EditorGUILayout.DelayedTextField("Target folder", PfxSettings.TargetFolder);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (PfxPaths.NormalizeAssetFolder(folder) != null) PfxSettings.TargetFolder = folder;
                        else Defer(() => SetStatus("The target folder must be inside Assets.", MessageType.Warning));
                    }
                    if (GUILayout.Button("Browse...", GUILayout.Width(70))) Defer(BrowseTargetFolder);
                }

                bool plan = HasPlan;
                var planTip = plan ? "" : "Needs a paid plan.";
                using (new EditorGUI.DisabledScope(!plan))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int scaleIndex = Mathf.Clamp(PfxSettings.Scale, 1, ScaleNames.Length) - 1;
                        int s = EditorGUILayout.Popup(new GUIContent("Scale", planTip), plan ? scaleIndex : 0, ScaleNames);
                        if (plan && s != scaleIndex) PfxSettings.Scale = s + 1;
                    }
                    int layoutIndex = plan && PfxSettings.PackedLayout ? 1 : 0;
                    int l = EditorGUILayout.Popup(new GUIContent("Sheet layout", plan ? "Packed trims empty space around each frame." : planTip), layoutIndex, LayoutNames);
                    if (plan && l != layoutIndex) PfxSettings.PackedLayout = l == 1;
                    bool n = EditorGUILayout.Toggle(new GUIContent("Normal map", plan ? "Adds _NormalMap as a sprite secondary texture for 2D lights." : planTip), plan && PfxSettings.NormalMap);
                    if (plan && n != PfxSettings.NormalMap) PfxSettings.NormalMap = n;
                }
                if (!plan)
                    EditorGUILayout.LabelField("Scale, packed sheets, normal maps and your own effects need a paid plan.", EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Import zip...", "Import a zip downloaded from particlefx.studio (Unity or Godot bundle)."))) Defer(ImportZipFromDisk);
                    if (GUILayout.Button("Open particlefx.studio")) Application.OpenURL(PfxApiRoutes.SiteUrl);
                }

                _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
                if (_showAdvanced)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _apiBaseField = EditorGUILayout.TextField("API base URL", _apiBaseField);
                        if (GUILayout.Button("Apply", GUILayout.Width(50)))
                        {
                            var apiBase = _apiBaseField;
                            Defer(() => SetApiBase(apiBase));
                        }
                        if (GUILayout.Button("Reset", GUILayout.Width(50)))
                            Defer(() => SetApiBase(PfxApiRoutes.DefaultApiBase));
                    }
                    EditorGUILayout.LabelField("Package " + PfxSettings.PackageVersion + ". Your API key is stored in this computer's Unity preferences, not in the project.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }
    }
}
