using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.EditorTools.Utility
{
    /// <summary>
    /// Measures the straight-line distance between two points picked in the Scene view.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneRuler
    {
        private const string MenuPath = "GhostHunter/Utility/Scene Ruler";
        private const float MarkerSize = 0.075f;

        private static readonly Color StartColor = new Color(0.15f, 0.9f, 1f, 1f);
        private static readonly Color EndColor = new Color(1f, 0.72f, 0.1f, 1f);
        private static readonly Rect OverlayRect = new Rect(12f, 12f, 300f, 168f);
        private static readonly int ControlHint = nameof(SceneRuler).GetHashCode();

        private static bool _isActive;
        private static bool _hasStart;
        private static bool _hasEnd;
        private static bool _lockX;
        private static bool _lockY;
        private static bool _lockZ;
        private static Vector3 _start;
        private static Vector3 _rawEnd;
        private static Vector3 _end;
        private static GUIStyle _distanceLabelStyle;

        static SceneRuler()
        {
            Menu.SetChecked(MenuPath, false);
        }

        [MenuItem(MenuPath, priority = 200)]
        private static void Toggle()
        {
            SetActive(!_isActive);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, _isActive);
            return true;
        }

        private static void SetActive(bool active)
        {
            if (_isActive == active)
                return;

            _isActive = active;
            if (_isActive)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                SceneView.lastActiveSceneView?.ShowNotification(
                    new GUIContent("Scene Ruler: click two points"));
            }
            else
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                ClearMeasurement();
            }

            Menu.SetChecked(MenuPath, _isActive);
            SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;
            bool isMouseOverOverlay = OverlayRect.Contains(current.mousePosition);

            DrawOverlay();
            if (!_isActive)
                return;

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                if (_hasStart || _hasEnd)
                    ClearMeasurement();
                else
                    SetActive(false);

                current.Use();
                SceneView.RepaintAll();
                return;
            }

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.R)
            {
                ClearMeasurement();
                current.Use();
                SceneView.RepaintAll();
                return;
            }

            int controlId = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
            if (current.type == EventType.Layout && !current.alt && !isMouseOverOverlay)
                HandleUtility.AddDefaultControl(controlId);

            Vector3 hoveredPoint = default;
            bool hasHoveredPoint = !isMouseOverOverlay
                && TryGetWorldPoint(current.mousePosition, out hoveredPoint);

            if (current.type == EventType.MouseDown
                && current.button == 0
                && !current.alt
                && !isMouseOverOverlay)
            {
                if (hasHoveredPoint)
                {
                    PlacePoint(hoveredPoint, sceneView);
                    current.Use();
                }
                else
                {
                    sceneView.ShowNotification(new GUIContent("No surface or Y=0 plane under cursor"));
                }
            }

            if (current.type == EventType.Repaint)
                DrawMeasurement(sceneView, hasHoveredPoint, hoveredPoint);

            if (current.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        private static bool TryGetWorldPoint(Vector2 mousePosition, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            object raycastResult = HandleUtility.RaySnap(ray);
            if (raycastResult is RaycastHit hit)
            {
                point = hit.point;
                return true;
            }

            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        private static void PlacePoint(Vector3 point, SceneView sceneView)
        {
            if (!_hasStart || _hasEnd)
            {
                _start = point;
                _hasStart = true;
                _hasEnd = false;
                sceneView.ShowNotification(new GUIContent("Start point set. Click the end point."));
            }
            else
            {
                _rawEnd = point;
                _end = ApplyAxisLocks(_rawEnd);
                _hasEnd = true;
                sceneView.ShowNotification(
                    new GUIContent($"Distance: {Vector3.Distance(_start, _end):0.###} m"));
            }

            SceneView.RepaintAll();
        }

        private static void DrawMeasurement(SceneView sceneView, bool hasHoveredPoint, Vector3 hoveredPoint)
        {
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;

            Handles.zTest = CompareFunction.Always;

            if (_hasStart)
                DrawMarker(_start, StartColor);

            if (_hasEnd)
            {
                DrawMarker(_end, EndColor);
                DrawDistanceLine(sceneView, _start, _end, 1f);
            }
            else if (_hasStart && hasHoveredPoint)
            {
                Vector3 constrainedPoint = ApplyAxisLocks(hoveredPoint);
                DrawMarker(constrainedPoint, EndColor);
                DrawDistanceLine(sceneView, _start, constrainedPoint, 0.65f);
            }
            else if (!_hasStart && hasHoveredPoint)
            {
                DrawMarker(hoveredPoint, StartColor);
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawMarker(Vector3 point, Color color)
        {
            Handles.color = color;
            float size = HandleUtility.GetHandleSize(point) * MarkerSize;
            Handles.SphereHandleCap(0, point, Quaternion.identity, size, EventType.Repaint);
        }

        private static void DrawDistanceLine(SceneView sceneView, Vector3 from, Vector3 to, float alpha)
        {
            Color lineColor = Color.Lerp(StartColor, EndColor, 0.5f);
            lineColor.a = alpha;
            Handles.color = lineColor;
            Handles.DrawAAPolyLine(4f, from, to);

            float distanceMeters = Vector3.Distance(from, to);
            Vector3 labelPosition = (from + to) * 0.5f;
            if (sceneView.camera != null)
            {
                labelPosition += sceneView.camera.transform.up
                    * HandleUtility.GetHandleSize(labelPosition)
                    * 0.08f;
            }

            string distanceLabel = $"{distanceMeters:0.###} m  ({distanceMeters * 100f:0.#} cm)";
            string lockedAxes = GetLockedAxes();
            if (lockedAxes.Length > 0)
                distanceLabel += $"\nLocked: {lockedAxes}";

            Handles.Label(labelPosition, distanceLabel, DistanceLabelStyle);
        }

        private static GUIStyle DistanceLabelStyle
        {
            get
            {
                if (_distanceLabelStyle != null)
                    return _distanceLabelStyle;

                _distanceLabelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                    normal =
                    {
                        textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black,
                    },
                };
                return _distanceLabelStyle;
            }
        }

        private static void DrawOverlay()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(OverlayRect, GUI.skin.window);
            GUILayout.Label("Scene Ruler", EditorStyles.boldLabel);

            string status = !_hasStart
                ? "Click the start point."
                : !_hasEnd
                    ? "Click the end point."
                    : $"Distance: {Vector3.Distance(_start, _end):0.###} m";
            GUILayout.Label(status);

            GUILayout.Label("Lock end-point coordinates to the start point:", EditorStyles.miniLabel);
            GUILayout.BeginHorizontal();
            bool lockX = GUILayout.Toggle(
                _lockX,
                new GUIContent("X Lock", "Keep the end point's X coordinate equal to the start point."),
                EditorStyles.miniButtonLeft);
            bool lockY = GUILayout.Toggle(
                _lockY,
                new GUIContent("Y Lock", "Keep the end point's Y coordinate equal to the start point."),
                EditorStyles.miniButtonMid);
            bool lockZ = GUILayout.Toggle(
                _lockZ,
                new GUIContent("Z Lock", "Keep the end point's Z coordinate equal to the start point."),
                EditorStyles.miniButtonRight);
            GUILayout.EndHorizontal();

            if (lockX != _lockX || lockY != _lockY || lockZ != _lockZ)
                SetAxisLocks(lockX, lockY, lockZ);

            GUILayout.Label("R / Esc: clear   |   Esc again: close", EditorStyles.miniLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear"))
            {
                ClearMeasurement();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Close"))
                SetActive(false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void ClearMeasurement()
        {
            _hasStart = false;
            _hasEnd = false;
            _start = default;
            _rawEnd = default;
            _end = default;
        }

        private static Vector3 ApplyAxisLocks(Vector3 point)
        {
            if (_lockX)
                point.x = _start.x;
            if (_lockY)
                point.y = _start.y;
            if (_lockZ)
                point.z = _start.z;
            return point;
        }

        private static void SetAxisLocks(bool lockX, bool lockY, bool lockZ)
        {
            _lockX = lockX;
            _lockY = lockY;
            _lockZ = lockZ;

            if (_hasEnd)
                _end = ApplyAxisLocks(_rawEnd);

            SceneView.RepaintAll();
        }

        private static string GetLockedAxes()
        {
            string axes = string.Empty;
            if (_lockX)
                axes = "X";
            if (_lockY)
                axes += axes.Length == 0 ? "Y" : ", Y";
            if (_lockZ)
                axes += axes.Length == 0 ? "Z" : ", Z";
            return axes;
        }
    }
}
