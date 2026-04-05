// Editor-only — must live inside an "Editor" folder.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EditorWindow for the procedural terrain generation system.
/// Open via: Tools > Terrain Generator
/// </summary>
public class TerrainGeneratorWindow : EditorWindow
{
    // ─── Constants ───────────────────────────────────────────────────────────

    private const string MenuPath      = "Tools/Terrain Generator/Open Window";
    private const string DefaultAssetPath = "Assets/ScriptableObjects/TerrainGeneratorSettings.asset";
    private const string DefaultMaskPath  = "Assets/TerrainMask.png";
    private const int    MaskResolution   = 1024;

    // ─── State ───────────────────────────────────────────────────────────────

    private TerrainGeneratorSettings _settings;
    private Terrain                  _terrain;

    private Vector2 _scrollPos;
    private bool    _generating;
    private string  _statusMessage = "Ready.";
    private float   _progressFraction;

    // Foldout states
    private bool _showBiomes   = true;
    private bool _showRoads    = true;
    private bool _showCanyons  = true;
    private bool _showMask     = true;
    private bool _showMaskPainter = false;

    // Mask painter state
    private enum MaskChannel { Exclusion, ForcedBiome, HeightClamp }
    private MaskChannel _paintChannel  = MaskChannel.Exclusion;
    private float       _brushSizeWorld = 20f;
    private float       _brushOpacity   = 1f;
    private bool        _erasing        = false;
    private Texture2D   _maskTexture;   // live-edited texture

    // Progress callback (marshalled to main thread)
    private (string message, float fraction) _progressReport;
    private bool _progressDirty;

    // ─── Open window ─────────────────────────────────────────────────────────

    [MenuItem(MenuPath)]
    public static void OpenWindow()
    {
        var win = GetWindow<TerrainGeneratorWindow>("Terrain Generator");
        win.minSize = new Vector2(360f, 500f);
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= OnEditorUpdate;
        EditorUtility.ClearProgressBar();
    }

    private void OnEditorUpdate()
    {
        if (!_progressDirty) return;
        _progressDirty = false;
        (string msg, float frac) = _progressReport;
        _statusMessage    = msg;
        _progressFraction = frac;
        if (_generating)
            EditorUtility.DisplayProgressBar("Terrain Generator", msg, frac);
        Repaint();
    }

    // ─── GUI ─────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawHeader();
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        DrawSettingsSection();
        EditorGUILayout.Space(4f);
        DrawBiomesSection();
        EditorGUILayout.Space(4f);
        DrawMaskSection();
        EditorGUILayout.Space(4f);
        DrawRoadsSection();
        EditorGUILayout.Space(4f);
        DrawCanyonsSection();
        EditorGUILayout.Space(8f);
        DrawGenerationButtons();
        EditorGUILayout.Space(4f);
        DrawStatusBar();

        EditorGUILayout.EndScrollView();

        if (_settings != null)
            EditorUtility.SetDirty(_settings);
    }

    // ─── Header ──────────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        EditorGUILayout.Space(4f);
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 16,
            alignment = TextAnchor.MiddleCenter
        };
        EditorGUILayout.LabelField("Terrain Generator", style, GUILayout.Height(28f));
        DrawSeparator();
    }

    // ─── Settings ────────────────────────────────────────────────────────────

    private void DrawSettingsSection()
    {
        EditorGUILayout.LabelField("Settings Asset", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        var newSettings = (TerrainGeneratorSettings)EditorGUILayout.ObjectField(
            _settings, typeof(TerrainGeneratorSettings), false);
        if (newSettings != _settings) BindSettings(newSettings);

        if (GUILayout.Button("New", GUILayout.Width(44f)))
            CreateNewSettings();
        EditorGUILayout.EndHorizontal();

        _terrain = (Terrain)EditorGUILayout.ObjectField("Target Terrain", _terrain, typeof(Terrain), true);

        if (_settings == null)
        {
            EditorGUILayout.HelpBox("Create or assign a TerrainGeneratorSettings asset to get started.", MessageType.Info);
            return;
        }

        // Seed row
        EditorGUILayout.BeginHorizontal();
        _settings.seed = EditorGUILayout.IntField("Seed", _settings.seed);
        if (GUILayout.Button("Randomize", GUILayout.Width(80f)))
        {
            _settings.seed = UnityEngine.Random.Range(0, int.MaxValue);
            EditorUtility.SetDirty(_settings);
        }
        EditorGUILayout.EndHorizontal();

        _settings.globalHeightScale  = EditorGUILayout.FloatField("Height Scale", _settings.globalHeightScale);
        _settings.globalNoiseOffset  = EditorGUILayout.Vector2Field("Noise Offset", _settings.globalNoiseOffset);
        _settings.splineSamples      = EditorGUILayout.IntSlider("Spline Samples", _settings.splineSamples, 50, 1000);
        _settings.colorMap           = (Texture2D)EditorGUILayout.ObjectField("Color Map", _settings.colorMap, typeof(Texture2D), false);

        if (GUI.changed) EditorUtility.SetDirty(_settings);
    }

    // ─── Biomes ──────────────────────────────────────────────────────────────

    private void DrawBiomesSection()
    {
        if (_settings == null) return;

        _showBiomes = EditorGUILayout.Foldout(_showBiomes, $"Biomes ({_settings.biomes.Count})", true, EditorStyles.foldoutHeader);
        if (!_showBiomes) return;

        EditorGUI.indentLevel++;

        for (int i = 0; i < _settings.biomes.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            string label = _settings.biomes[i] != null ? _settings.biomes[i].biomeName : "(empty)";
            EditorGUILayout.LabelField($"{i}: {label}", GUILayout.ExpandWidth(true));

            _settings.biomes[i] = (BiomeDefinition)EditorGUILayout.ObjectField(
                _settings.biomes[i], typeof(BiomeDefinition), false, GUILayout.Width(160f));

            if (GUILayout.Button("▲", GUILayout.Width(22f)) && i > 0)
            {
                (_settings.biomes[i], _settings.biomes[i - 1]) = (_settings.biomes[i - 1], _settings.biomes[i]);
                EditorUtility.SetDirty(_settings);
            }
            if (GUILayout.Button("▼", GUILayout.Width(22f)) && i < _settings.biomes.Count - 1)
            {
                (_settings.biomes[i], _settings.biomes[i + 1]) = (_settings.biomes[i + 1], _settings.biomes[i]);
                EditorUtility.SetDirty(_settings);
            }
            if (GUILayout.Button("✕", GUILayout.Width(22f)))
            {
                _settings.biomes.RemoveAt(i);
                EditorUtility.SetDirty(_settings);
                break;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Biome Slot"))
        {
            _settings.biomes.Add(null);
            EditorUtility.SetDirty(_settings);
        }
        if (GUILayout.Button("Create New Biome Asset"))
            CreateNewBiome();
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    // ─── Mask ────────────────────────────────────────────────────────────────

    private void DrawMaskSection()
    {
        if (_settings == null) return;

        _showMask = EditorGUILayout.Foldout(_showMask, "Terrain Mask", true, EditorStyles.foldoutHeader);
        if (!_showMask) return;

        EditorGUI.indentLevel++;

        EditorGUILayout.BeginHorizontal();
        _settings.terrainMask = (Texture2D)EditorGUILayout.ObjectField(
            "Mask Texture", _settings.terrainMask, typeof(Texture2D), false);

        if (GUILayout.Button("Create", GUILayout.Width(55f)))
            CreateMaskTexture();
        if (GUILayout.Button("Clear", GUILayout.Width(45f)) && _settings.terrainMask != null)
            ClearMaskTexture();
        EditorGUILayout.EndHorizontal();

        _settings.maskForcedBiomeIndex = EditorGUILayout.IntField("Forced Biome Index", _settings.maskForcedBiomeIndex);
        _settings.maskHeightClamp      = EditorGUILayout.Slider("Height Clamp", _settings.maskHeightClamp, 0f, 1f);

        // ─── Mask Painter ────────────────────────────────────────────────────
        _showMaskPainter = EditorGUILayout.Foldout(_showMaskPainter, "Mask Painter", true);
        if (_showMaskPainter)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Select a paint mode then click-drag in the SceneView to paint the mask.\n" +
                "Hold Alt to erase.",
                MessageType.Info);

            _paintChannel   = (MaskChannel)EditorGUILayout.EnumPopup("Paint Channel", _paintChannel);
            _brushSizeWorld = EditorGUILayout.Slider("Brush Size (world units)", _brushSizeWorld, 1f, 200f);
            _brushOpacity   = EditorGUILayout.Slider("Brush Opacity", _brushOpacity, 0.01f, 1f);

            if (GUILayout.Button("Save Mask Texture"))
                SaveMaskTexture();

            EditorGUI.indentLevel--;
        }

        if (GUI.changed) EditorUtility.SetDirty(_settings);
        EditorGUI.indentLevel--;
    }

    // ─── Roads ───────────────────────────────────────────────────────────────

    private void DrawRoadsSection()
    {
        if (_settings == null) return;

        _showRoads = EditorGUILayout.Foldout(_showRoads, $"Roads ({_settings.roads.Count})", true, EditorStyles.foldoutHeader);
        if (!_showRoads) return;

        EditorGUI.indentLevel++;

        for (int i = 0; i < _settings.roads.Count; i++)
        {
            var road = _settings.roads[i];
            if (road == null) { _settings.roads[i] = new RoadDefinition(); road = _settings.roads[i]; }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Road {i}: {road.roadName}", EditorStyles.boldLabel);
            if (GUILayout.Button("✕", GUILayout.Width(22f)))
            {
                _settings.roads.RemoveAt(i);
                EditorUtility.SetDirty(_settings);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            road.roadName        = EditorGUILayout.TextField("Name", road.roadName);
            road.width           = EditorGUILayout.FloatField("Width (m)", road.width);
            road.flatness        = EditorGUILayout.Slider("Flatness", road.flatness, 0f, 1f);
            road.smoothingFalloff = EditorGUILayout.FloatField("Smoothing Falloff (m)", road.smoothingFalloff);
            road.textureLayerIndex = EditorGUILayout.IntField("Texture Layer Index", road.textureLayerIndex);
            road.camber          = EditorGUILayout.Slider("Camber", road.camber, -0.15f, 0.15f);

            DrawControlPoints(ref road.controlPoints, $"road_{i}");

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Road"))
        {
            _settings.roads.Add(new RoadDefinition());
            EditorUtility.SetDirty(_settings);
        }

        if (GUI.changed) EditorUtility.SetDirty(_settings);
        EditorGUI.indentLevel--;
    }

    // ─── Canyons ─────────────────────────────────────────────────────────────

    private void DrawCanyonsSection()
    {
        if (_settings == null) return;

        _showCanyons = EditorGUILayout.Foldout(_showCanyons, $"Canyons ({_settings.canyons.Count})", true, EditorStyles.foldoutHeader);
        if (!_showCanyons) return;

        EditorGUI.indentLevel++;

        for (int i = 0; i < _settings.canyons.Count; i++)
        {
            var canyon = _settings.canyons[i];
            if (canyon == null) { _settings.canyons[i] = new CanyonDefinition(); canyon = _settings.canyons[i]; }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Canyon {i}: {canyon.canyonName}", EditorStyles.boldLabel);
            if (GUILayout.Button("✕", GUILayout.Width(22f)))
            {
                _settings.canyons.RemoveAt(i);
                EditorUtility.SetDirty(_settings);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            canyon.canyonName        = EditorGUILayout.TextField("Name", canyon.canyonName);
            canyon.depth             = EditorGUILayout.FloatField("Depth (m)", canyon.depth);
            canyon.width             = EditorGUILayout.FloatField("Width (m)", canyon.width);
            canyon.wallProfile       = EditorGUILayout.Slider("Wall Profile", canyon.wallProfile, 0f, 1f);
            canyon.wallCurve         = EditorGUILayout.CurveField("Wall Curve (override)", canyon.wallCurve);
            canyon.smoothness        = EditorGUILayout.Slider("Rim Smoothness", canyon.smoothness, 0f, 1f);
            canyon.flatFloor         = EditorGUILayout.Toggle("Flat Floor", canyon.flatFloor);
            if (canyon.flatFloor)
                canyon.floorFraction = EditorGUILayout.Slider("Floor Fraction", canyon.floorFraction, 0f, 1f);
            canyon.erosionNoiseFrequency = EditorGUILayout.FloatField("Erosion Frequency", canyon.erosionNoiseFrequency);
            canyon.erosionNoiseAmplitude = EditorGUILayout.FloatField("Erosion Amplitude", canyon.erosionNoiseAmplitude);

            DrawControlPoints(ref canyon.controlPoints, $"canyon_{i}");

            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Canyon"))
        {
            _settings.canyons.Add(new CanyonDefinition());
            EditorUtility.SetDirty(_settings);
        }

        if (GUI.changed) EditorUtility.SetDirty(_settings);
        EditorGUI.indentLevel--;
    }

    // ─── Control point editor ─────────────────────────────────────────────────

    private void DrawControlPoints(ref Vector3[] controlPoints, string key)
    {
        if (controlPoints == null) controlPoints = new Vector3[] { Vector3.zero, new Vector3(0, 0, 100f) };

        EditorGUILayout.LabelField("Control Points", EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();
            controlPoints[i] = EditorGUILayout.Vector3Field($"[{i}]", controlPoints[i]);
            if (GUILayout.Button("✕", GUILayout.Width(22f)) && controlPoints.Length > 2)
            {
                var list = new System.Collections.Generic.List<Vector3>(controlPoints);
                list.RemoveAt(i);
                controlPoints = list.ToArray();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Point"))
        {
            var list = new System.Collections.Generic.List<Vector3>(controlPoints);
            var last = list[list.Count - 1];
            list.Add(last + Vector3.forward * 50f);
            controlPoints = list.ToArray();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    // ─── Generation buttons ───────────────────────────────────────────────────

    private void DrawGenerationButtons()
    {
        if (_settings == null || _terrain == null)
        {
            EditorGUILayout.HelpBox("Assign a Settings asset and a Target Terrain to generate.", MessageType.Warning);
            return;
        }

        EditorGUI.BeginDisabledGroup(_generating);

        // Main generate button
        var bigStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize    = 14,
            fontStyle   = FontStyle.Bold,
            fixedHeight = 36f
        };
        var savedColor = GUI.backgroundColor;
        GUI.backgroundColor = _generating ? Color.gray : new Color(0.3f, 0.85f, 0.3f);
        if (GUILayout.Button("⚡ Regenerate All", bigStyle))
            RunAsync(() => TerrainGenerator.GenerateAsync(_settings, _terrain, MakeProgress()));
        GUI.backgroundColor = savedColor;

        EditorGUILayout.Space(4f);

        // Partial regeneration
        EditorGUILayout.LabelField("Partial Regeneration", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Heightmap Only"))
            RunAsync(() => TerrainGenerator.GenerateHeightmapAsync(_settings, _terrain, MakeProgress()));

        if (GUILayout.Button("Splatmap Only"))
            RunAsync(() => TerrainGenerator.GenerateSplatmapAsync(_settings, _terrain, MakeProgress()));

        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();
    }

    // ─── Status bar ──────────────────────────────────────────────────────────

    private void DrawStatusBar()
    {
        if (_generating)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18f), _progressFraction, _statusMessage);
        }
        else
        {
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.centeredGreyMiniLabel);
        }
    }

    // ─── Async runner ────────────────────────────────────────────────────────

    private async void RunAsync(Func<Task> job)
    {
        if (_generating) return;
        _generating       = true;
        _progressFraction = 0f;
        _statusMessage    = "Starting...";
        Repaint();

        try
        {
            await job();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TerrainGeneratorWindow] Generation failed: {ex}");
            _statusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _generating = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }
    }

    private IProgress<(string, float)> MakeProgress()
    {
        return new Progress<(string message, float fraction)>(report =>
        {
            _progressReport = report;
            _progressDirty  = true;
        });
    }

    // ─── Scene GUI (mask painter) ─────────────────────────────────────────────

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!_showMaskPainter || _settings == null || _terrain == null) return;

        Event e = Event.current;

        // Draw brush circle in scene
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.Repaint)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Handles.color = _erasing
                    ? new Color(1f, 0.2f, 0.2f, 0.7f)
                    : new Color(0.2f, 1f, 0.4f, 0.7f);
                Handles.DrawWireDisc(hit.point, hit.normal, _brushSizeWorld * 0.5f);
                sceneView.Repaint();
            }
        }

        // Eat mouse events while painting
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && !e.alt)
        {
            _erasing = e.control;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var tc = hit.collider as TerrainCollider;
                if (tc != null)
                {
                    EnsureMaskTexture();
                    PaintMask(hit.point);
                    e.Use();
                }
            }
        }

        // Block selection while painting
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
    }

    // ─── Mask painting ────────────────────────────────────────────────────────

    private void EnsureMaskTexture()
    {
        if (_maskTexture != null) return;

        if (_settings.terrainMask != null)
        {
            // Load a readable copy
            var path = AssetDatabase.GetAssetPath(_settings.terrainMask);
            _maskTexture = LoadReadableTexture(_settings.terrainMask);
            return;
        }

        // Create a new blank mask
        CreateMaskTexture();
    }

    private void PaintMask(Vector3 worldHit)
    {
        if (_maskTexture == null || _terrain == null) return;

        var td  = _terrain.terrainData;
        var pos = _terrain.transform.position;

        // World hit → UV on terrain
        float u = (worldHit.x - pos.x) / td.size.x;
        float v = (worldHit.z - pos.z) / td.size.z;

        // UV → texel
        int cx = Mathf.Clamp(Mathf.RoundToInt(u * _maskTexture.width),  0, _maskTexture.width  - 1);
        int cy = Mathf.Clamp(Mathf.RoundToInt(v * _maskTexture.height), 0, _maskTexture.height - 1);

        // Brush radius in pixels
        float brushRadiusPx = (_brushSizeWorld / td.size.x) * _maskTexture.width * 0.5f;
        int   radiusI       = Mathf.CeilToInt(brushRadiusPx);

        byte paintValue = _erasing ? (byte)0 : (byte)255;
        float opacity = _brushOpacity;

        for (int dy = -radiusI; dy <= radiusI; dy++)
        {
            for (int dx = -radiusI; dx <= radiusI; dx++)
            {
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > brushRadiusPx) continue;

                int px = cx + dx; int py = cy + dy;
                if (px < 0 || px >= _maskTexture.width || py < 0 || py >= _maskTexture.height) continue;

                float falloff = 1f - (dist / brushRadiusPx);
                float alpha   = opacity * falloff;

                Color32 current = _maskTexture.GetPixel(px, py);
                byte    target  = paintValue;

                switch (_paintChannel)
                {
                    case MaskChannel.Exclusion:
                        current.r = (byte)Mathf.RoundToInt(Mathf.Lerp(current.r, target, alpha));
                        break;
                    case MaskChannel.ForcedBiome:
                        current.g = (byte)Mathf.RoundToInt(Mathf.Lerp(current.g, target, alpha));
                        break;
                    case MaskChannel.HeightClamp:
                        current.b = (byte)Mathf.RoundToInt(Mathf.Lerp(current.b, target, alpha));
                        break;
                }
                _maskTexture.SetPixel(px, py, current);
            }
        }

        _maskTexture.Apply();
        _settings.terrainMask = _maskTexture;
    }

    private void SaveMaskTexture()
    {
        if (_maskTexture == null) { Debug.LogWarning("[TerrainGeneratorWindow] No mask texture to save."); return; }

        byte[] png = _maskTexture.EncodeToPNG();

        string path = _settings.terrainMask != null
            ? AssetDatabase.GetAssetPath(_settings.terrainMask)
            : DefaultMaskPath;

        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", path), png);
        AssetDatabase.Refresh();

        _settings.terrainMask = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        EditorUtility.SetDirty(_settings);
        Debug.Log($"[TerrainGeneratorWindow] Mask saved to {path}.");
    }

    private void CreateMaskTexture()
    {
        _maskTexture = new Texture2D(MaskResolution, MaskResolution, TextureFormat.RGB24, false);
        Color32[] pixels = new Color32[MaskResolution * MaskResolution];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
        _maskTexture.SetPixels32(pixels);
        _maskTexture.Apply();

        string dir = Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", DefaultMaskPath));
        Directory.CreateDirectory(dir);

        byte[] png = _maskTexture.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", DefaultMaskPath), png);
        AssetDatabase.Refresh();

        string assetPath = DefaultMaskPath;
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer != null)
        {
            importer.isReadable        = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        _settings.terrainMask = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        EditorUtility.SetDirty(_settings);
        Debug.Log($"[TerrainGeneratorWindow] Mask texture created at {assetPath}.");
    }

    private void ClearMaskTexture()
    {
        if (_settings.terrainMask == null) return;
        var path = AssetDatabase.GetAssetPath(_settings.terrainMask);

        Texture2D tex = LoadReadableTexture(_settings.terrainMask);
        Color32[] pixels = new Color32[tex.width * tex.height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
        tex.SetPixels32(pixels);
        tex.Apply();

        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", path), tex.EncodeToPNG());
        AssetDatabase.Refresh();
        _maskTexture = null;
        Debug.Log("[TerrainGeneratorWindow] Mask cleared.");
    }

    private static Texture2D LoadReadableTexture(Texture2D src)
    {
        // Creates a readable copy of a texture using a RenderTexture blit
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }

    // ─── Asset helpers ────────────────────────────────────────────────────────

    private void BindSettings(TerrainGeneratorSettings newSettings)
    {
        _settings    = newSettings;
        _maskTexture = null;
    }

    private void CreateNewSettings()
    {
        EnsureDirectory(Path.GetDirectoryName(DefaultAssetPath));

        var asset = CreateInstance<TerrainGeneratorSettings>();
        AssetDatabase.CreateAsset(asset, GenerateUniquePath(DefaultAssetPath));
        AssetDatabase.SaveAssets();
        BindSettings(asset);
        EditorGUIUtility.PingObject(asset);
        Debug.Log("[TerrainGeneratorWindow] Created new TerrainGeneratorSettings.");
    }

    private void CreateNewBiome()
    {
        string dir = "Assets/ScriptableObjects/Biomes";
        EnsureDirectory(dir);
        var asset = CreateInstance<BiomeDefinition>();
        asset.biomeName = "New Biome";
        string path = GenerateUniquePath($"{dir}/NewBiome.asset");
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        _settings.biomes.Add(asset);
        EditorUtility.SetDirty(_settings);
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[TerrainGeneratorWindow] Created biome asset at {path}.");
    }

    private static void EnsureDirectory(string unityRelPath)
    {
        string full = Path.Combine(Application.dataPath, "..", unityRelPath);
        Directory.CreateDirectory(full);
        AssetDatabase.Refresh();
    }

    private static string GenerateUniquePath(string path)
    {
        return AssetDatabase.GenerateUniqueAssetPath(path);
    }

    // ─── Utility ──────────────────────────────────────────────────────────────

    private static void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1f);
        rect.height = 1f;
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(2f);
    }
}
#endif
