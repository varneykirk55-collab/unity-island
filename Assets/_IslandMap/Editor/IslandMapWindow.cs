// Editor-only developer map viewer (never part of a build).
// Tools > Island > Map > Developer Map
//  - hover: live world X / Z / ground Y / grid cell readout (Y from the open terrain, or from IslandMapData)
//  - click: pin a point; pins are drawn in the Scene view too; copy coordinates or frame the Scene view on them
//  - go to: type world X/Z or a cell name (e.g. "D6") to pin it
//  - markers: shows Docs/IslandMap/markers.json (owner annotations) and reports problems in it
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace IslandMap.Editor
{
    public class IslandMapWindow : EditorWindow
    {
        enum View { Developer, Overhead, Analysis }

        [SerializeField] View _view = View.Developer;
        [SerializeField] float _zoom = -1f;
        [SerializeField] Vector2 _pan;
        [SerializeField] List<Vector3> _pins = new List<Vector3>();
        [SerializeField] float _gotoX, _gotoZ;
        [SerializeField] string _gotoCell = "";
        [SerializeField] bool _showMarkers = true;

        IslandMapData _data;
        Texture2D _tex;
        View _texView = (View)(-1);
        MapMarkerSet _markers;
        List<string> _markerIssues = new List<string>();
        Vector3? _hover;
        Vector2 _scroll;

        [MenuItem("Tools/Island/Map/Developer Map")]
        static void Open() => GetWindow<IslandMapWindow>("Island Dev Map").Show();

        void OnEnable() { Reload(); SceneView.duringSceneGui += OnSceneGUI; wantsMouseMove = true; }
        void OnDisable() { SceneView.duringSceneGui -= OnSceneGUI; if (_tex) DestroyImmediate(_tex); }

        string ImagePath(View v) => Path.Combine(IslandMapGenerator.DefaultOut,
            v == View.Developer ? IslandMapGenerator.DevMapFile : v == View.Analysis ? IslandMapGenerator.AnalysisFile : IslandMapGenerator.OverheadFile);

        MapProjection Proj => _view == View.Developer ? _data.DeveloperMap : _view == View.Analysis ? _data.Analysis : _data.Overhead;

        void Reload()
        {
            _data = AssetDatabase.LoadAssetAtPath<IslandMapData>(IslandMapGenerator.DataAssetPath);
            if (_tex) DestroyImmediate(_tex);
            _tex = null; _texView = (View)(-1);
            string mp = Path.Combine(IslandMapGenerator.DefaultOut, IslandMapGenerator.MarkersFile);
            _markers = File.Exists(mp) ? MapMarkerSet.FromJson(File.ReadAllText(mp)) : new MapMarkerSet();
            _markerIssues = _data ? _markers.Validate(_data.Overhead) : new List<string>();
        }

        void EnsureTexture()
        {
            if (_tex && _texView == _view) return;
            if (_tex) DestroyImmediate(_tex);
            _tex = null;
            string p = ImagePath(_view);
            if (!File.Exists(p)) return;
            _tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            _tex.LoadImage(File.ReadAllBytes(p));
            _texView = _view;
        }

        float GroundY(Vector3 w)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (w.x >= p.x && w.z >= p.z && w.x <= p.x + s.x && w.z <= p.z + s.z) return t.SampleHeight(w) + p.y;
            }
            return _data ? _data.SampleHeight(w) : float.NaN;
        }

        string Describe(Vector3 w)
        {
            float y = GroundY(w);
            return $"X {w.x:F1}   Z {w.z:F1}   " + (float.IsNaN(y) ? "" : $"ground Y {y:F1}   ") + $"cell {_data.Grid.CellName(w)}";
        }

        Vector3 WithGround(Vector2 xz) { var w = new Vector3(xz.x, 0, xz.y); float y = GroundY(w); w.y = float.IsNaN(y) ? 0 : y; return w; }

        void OnGUI()
        {
            if (!_data)
            {
                EditorGUILayout.HelpBox("No map data yet. Run Tools > Island > Map > Generate Maps (Scene 1).", MessageType.Info);
                if (GUILayout.Button("Reload")) Reload();
                return;
            }
            EnsureTexture();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var v = (View)EditorGUILayout.EnumPopup(_view, EditorStyles.toolbarDropDown, GUILayout.Width(110));
                if (v != _view) { _view = v; _zoom = -1; }
                if (GUILayout.Button("Fit", EditorStyles.toolbarButton, GUILayout.Width(40))) _zoom = -1;
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(55))) { Reload(); EnsureTexture(); }
                _showMarkers = GUILayout.Toggle(_showMarkers, $"Markers ({_markers.Markers.Count})", EditorStyles.toolbarButton, GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_data.SourceScene}  ·  generated {_data.GeneratedUtc}", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Go to  X", GUILayout.Width(52)); _gotoX = EditorGUILayout.FloatField(_gotoX, GUILayout.Width(70));
                GUILayout.Label("Z", GUILayout.Width(12)); _gotoZ = EditorGUILayout.FloatField(_gotoZ, GUILayout.Width(70));
                if (GUILayout.Button("Pin", GUILayout.Width(40))) Pin(WithGround(new Vector2(_gotoX, _gotoZ)));
                GUILayout.Space(16);
                GUILayout.Label("Cell", GUILayout.Width(30)); _gotoCell = EditorGUILayout.TextField(_gotoCell, GUILayout.Width(50));
                if (GUILayout.Button("Pin centre", GUILayout.Width(80)) && _data.Grid.TryParseCell(_gotoCell, out var cell)) Pin(WithGround(_data.Grid.CellRect(cell).center));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.LabelField(_hover.HasValue ? Describe(_hover.Value) : "Hover over the map for world coordinates. Click to pin. Scroll to zoom, right/middle-drag to pan.", EditorStyles.boldLabel);

            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMap(area);

            if (_markerIssues.Count > 0) EditorGUILayout.HelpBox("markers.json: " + string.Join("\n", _markerIssues), MessageType.Warning);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(140, 26 + _pins.Count * 22)));
            for (int i = 0; i < _pins.Count; i++)
            {
                var w = _pins[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"P{i + 1}", GUILayout.Width(28));
                    GUILayout.Label(Describe(w));
                    if (GUILayout.Button("Copy X,Y,Z", GUILayout.Width(85))) EditorGUIUtility.systemCopyBuffer = $"{w.x:F2}, {w.y:F2}, {w.z:F2}";
                    if (GUILayout.Button("Frame in Scene", GUILayout.Width(105))) Frame(w);
                    if (GUILayout.Button("x", GUILayout.Width(22))) { _pins.RemoveAt(i); SceneView.RepaintAll(); GUIUtility.ExitGUI(); }
                }
            }
            if (_pins.Count > 0 && GUILayout.Button("Clear pins", GUILayout.Width(90))) { _pins.Clear(); SceneView.RepaintAll(); }
            EditorGUILayout.EndScrollView();
        }

        void Pin(Vector3 w) { _pins.Add(w); SceneView.RepaintAll(); Repaint(); }

        static void Frame(Vector3 w)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv) sv.LookAt(w, Quaternion.Euler(50f, 0f, 0f), 80f);
        }

        void DrawMap(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f));
            if (!_tex) { GUI.Label(area, "  Image not found: " + ImagePath(_view)); return; }
            var size = new Vector2(_tex.width, _tex.height);
            if (_zoom <= 0) { _zoom = Mathf.Min(area.width / size.x, area.height / size.y); _pan = (area.size - size * _zoom) * 0.5f; }

            var e = Event.current;
            Vector2 local = e.mousePosition - area.position;
            if (area.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    float z = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.2f : 1f / 1.2f), 0.02f, 8f);
                    _pan = local - (local - _pan) * (z / _zoom); _zoom = z; e.Use();
                }
                else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2)) { _pan += e.delta; e.Use(); }
                else if (e.type == EventType.MouseDown && e.button == 0 && _hover.HasValue) { Pin(_hover.Value); e.Use(); }
                var px = (local - _pan) / _zoom;
                _hover = Proj.ContainsPixel(px) ? WithGround(Proj.PixelToWorldXZ(px)) : (Vector3?)null;
                if (e.type == EventType.MouseMove) Repaint();
            }
            else if (e.type == EventType.MouseMove) _hover = null;

            GUI.BeginClip(area);
            GUI.DrawTexture(new Rect(_pan, size * _zoom), _tex);
            Vector2 ToGui(Vector3 w) => _pan + Proj.WorldToPixel(w) * _zoom;
            if (_showMarkers)
                foreach (var m in _markers.Markers)
                {
                    var g = ToGui(m.Position);
                    if (m.Zone != null)
                        for (int i = 0; i < m.Zone.Count; i++)
                        {
                            var a = _pan + Proj.WorldToPixel(m.Zone[i]) * _zoom; var b = _pan + Proj.WorldToPixel(m.Zone[(i + 1) % m.Zone.Count]) * _zoom;
                            Handles.color = new Color(0.3f, 0.9f, 1f); Handles.DrawAAPolyLine(3f, a, b);
                        }
                    EditorGUI.DrawRect(new Rect(g.x - 5, g.y - 5, 10, 10), new Color(0.3f, 0.9f, 1f));
                    GUI.Label(new Rect(g.x + 8, g.y - 9, 300, 18), $"{m.Name} [{m.Status}]", EditorStyles.whiteBoldLabel);
                }
            for (int i = 0; i < _pins.Count; i++)
            {
                var g = ToGui(_pins[i]);
                EditorGUI.DrawRect(new Rect(g.x - 6, g.y - 1, 12, 2), Color.magenta);
                EditorGUI.DrawRect(new Rect(g.x - 1, g.y - 6, 2, 12), Color.magenta);
                GUI.Label(new Rect(g.x + 6, g.y - 18, 60, 18), "P" + (i + 1), EditorStyles.whiteBoldLabel);
            }
            GUI.EndClip();
        }

        void OnSceneGUI(SceneView sv)
        {
            if (_pins.Count == 0) return;
            Handles.color = Color.magenta;
            for (int i = 0; i < _pins.Count; i++)
            {
                var w = _pins[i];
                Handles.DrawLine(w, w + Vector3.up * 30f, 3f);
                Handles.DrawWireDisc(w, Vector3.up, 3f, 3f);
                Handles.Label(w + Vector3.up * 32f, $"P{i + 1}  X {w.x:F0} Z {w.z:F0}", EditorStyles.whiteBoldLabel);
            }
        }
    }
}
