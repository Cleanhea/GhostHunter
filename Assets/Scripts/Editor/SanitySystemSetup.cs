using System;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Systems.Installers;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>정신력 설정·플레이어 상태·Game 씬 팀 서비스를 반복 가능하게 설치한다.</summary>
    public static class SanitySystemSetup
    {
        internal const string SettingsPath =
            "Assets/Settings/Gameplay/SanitySystemSettings_Default.asset";

        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string SystemObjectName = "SanitySystem";
        private const string LegacyHudCanvasObjectName = "SanityHudCanvas";
        private const string MonitorObjectName = "SanityWorldMonitor";

        private static readonly Vector3 MonitorPosition = new(0f, 4.25f, 10.25f);
        private static readonly Vector3 MonitorScale = Vector3.one * 0.006f;
        private static readonly Vector2 MonitorResolution = new(960f, 540f);

        [MenuItem("GhostHunter/정신력 시스템 설치", priority = 4)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the sanity system.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the sanity system. " +
                    $"The active scene is '{scene.path}'.");
            }

            SanitySystemSettings settings = LoadOrCreateSettings();
            InstallPlayerState(settings);
            InstallSceneService(scene);
            InstallWorldMonitor(scene);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            ValidateInstallation();

            if (logCompletion)
            {
                Debug.Log(
                    "[SanitySystemSetup] 정신력 설정·Player 프리팹·" +
                    "Game 씬 팀 서비스·World Space 정신력 모니터 설치 완료.");
            }
        }

        internal static void ValidateInstallation()
        {
            SanitySystemSettings settings =
                AssetDatabase.LoadAssetAtPath<SanitySystemSettings>(SettingsPath);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            if (settings == null)
                throw new MissingReferenceException($"정신력 설정 에셋이 없습니다: {SettingsPath}");
            if (player == null)
                throw new MissingReferenceException($"Player 프리팹이 없습니다: {PlayerPrefabPath}");

            SanityNetworkState state = player.GetComponent<SanityNetworkState>();
            if (state == null)
                throw new MissingComponentException("Player 프리팹에 SanityNetworkState가 없습니다.");

            var serializedState = new SerializedObject(state);
            if (serializedState.FindProperty("_settings").objectReferenceValue != settings)
                throw new MissingReferenceException("Player 정신력 설정 배선이 잘못됐습니다.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                return;

            FindSceneBindings(scene, out GameInstaller installer, out SanityTeamService service);
            if (installer == null || service == null)
                throw new MissingReferenceException("Game 씬의 정신력 서비스 배선이 없습니다.");

            var serializedInstaller = new SerializedObject(installer);
            if (serializedInstaller.FindProperty("_sanityTeam").objectReferenceValue != service)
                throw new MissingReferenceException("GameInstaller 정신력 서비스 배선이 잘못됐습니다.");

            ValidateWorldMonitor(scene);
        }

        private static SanitySystemSettings LoadOrCreateSettings()
        {
            SanitySystemSettings settings =
                AssetDatabase.LoadAssetAtPath<SanitySystemSettings>(SettingsPath);
            if (settings != null)
                return settings;

            settings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static void InstallPlayerState(SanitySystemSettings settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                SanityNetworkState state = root.GetComponent<SanityNetworkState>();
                if (state == null)
                    state = root.AddComponent<SanityNetworkState>();

                SetObjectReference(state, "_settings", settings);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void InstallSceneService(Scene scene)
        {
            FindSceneBindings(scene, out GameInstaller installer, out SanityTeamService service);
            if (installer == null)
                throw new MissingReferenceException("Game 씬에서 GameInstaller를 찾지 못했습니다.");

            if (service == null)
            {
                GameObject systemObject = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == SystemObjectName)
                    {
                        systemObject = root;
                        break;
                    }
                }

                if (systemObject == null)
                    systemObject = new GameObject(SystemObjectName);

                service = systemObject.GetComponent<SanityTeamService>();
                if (service == null)
                    service = systemObject.AddComponent<SanityTeamService>();
            }

            SetObjectReference(installer, "_sanityTeam", service);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void InstallWorldMonitor(Scene scene)
        {
            GameObject legacyHud = FindSceneRoot(scene, LegacyHudCanvasObjectName);
            if (legacyHud != null)
                Object.DestroyImmediate(legacyHud);

            GameObject canvasObject = FindSceneRoot(scene, MonitorObjectName);
            if (canvasObject == null)
            {
                canvasObject = new GameObject(MonitorObjectName, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
            }

            ClearChildren(canvasObject.transform);

            canvasObject.layer = LayerMask.NameToLayer("UI");
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.SetPositionAndRotation(MonitorPosition, Quaternion.identity);
            canvasRect.sizeDelta = MonitorResolution;
            canvasRect.localScale = MonitorScale;

            Canvas canvas = GetOrAddComponent<Canvas>(canvasObject);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 0;
            canvas.worldCamera = null;

            CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(canvasObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = 10f;

            GameObject panel = GetOrCreateUiChild(canvasObject.transform, "SanityPanel");
            ConfigureFixedRect(
                panel.GetComponent<RectTransform>(),
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                Vector2.zero,
                new Vector2(920f, 500f));
            ConfigureImage(panel, new Color(0.008f, 0.012f, 0.02f, 0.98f));

            CreateFrameLine(panel.transform, "FrameTop", new Vector2(0f, 247f), new Vector2(920f, 6f));
            CreateFrameLine(panel.transform, "FrameBottom", new Vector2(0f, -247f), new Vector2(920f, 6f));
            CreateFrameLine(panel.transform, "FrameLeft", new Vector2(-457f, 0f), new Vector2(6f, 500f));
            CreateFrameLine(panel.transform, "FrameRight", new Vector2(457f, 0f), new Vector2(6f, 500f));

            Text title = ConfigureText(
                GetOrCreateUiChild(panel.transform, "Title"),
                "정신력 현황",
                38,
                FontStyle.Bold,
                new Color(0.76f, 0.9f, 1f),
                TextAnchor.MiddleCenter);
            ConfigureCenteredRect(title.rectTransform, new Vector2(0f, 190f), new Vector2(500f, 60f));

            GameObject titleLine = GetOrCreateUiChild(panel.transform, "TitleLine");
            ConfigureCenteredRect(
                titleLine.GetComponent<RectTransform>(),
                new Vector2(0f, 151f),
                new Vector2(680f, 3f));
            ConfigureImage(titleLine, new Color(0.58f, 0.72f, 0.82f, 0.8f));

            Text[] playerLabels = new Text[4];
            Text[] playerSymbols = new Text[4];
            Text[] playerValues = new Text[4];
            Vector2[] playerSlotPositions =
            {
                new(-225f, 75f),
                new(225f, 75f),
                new(-225f, -30f),
                new(225f, -30f),
            };
            for (int i = 0; i < playerSlotPositions.Length; i++)
            {
                CreatePlayerSlot(
                    panel.transform,
                    i,
                    playerSlotPositions[i],
                    out playerLabels[i],
                    out playerSymbols[i],
                    out playerValues[i]);
            }

            GameObject teamDivider = GetOrCreateUiChild(panel.transform, "TeamDivider");
            ConfigureCenteredRect(
                teamDivider.GetComponent<RectTransform>(),
                new Vector2(0f, -100f),
                new Vector2(680f, 2f));
            ConfigureImage(teamDivider, new Color(0.28f, 0.34f, 0.4f, 0.75f));

            Text teamLabel = ConfigureText(
                GetOrCreateUiChild(panel.transform, "TeamLabel"),
                "팀 평균",
                28,
                FontStyle.Bold,
                new Color(0.55f, 0.59f, 0.63f, 1f),
                TextAnchor.MiddleRight);
            ConfigureCenteredRect(
                teamLabel.rectTransform,
                new Vector2(-115f, -165f),
                new Vector2(180f, 60f));

            Text teamSymbol = ConfigureText(
                GetOrCreateUiChild(panel.transform, "TeamSymbol"),
                "?",
                48,
                FontStyle.Normal,
                new Color(0.55f, 0.59f, 0.63f, 1f),
                TextAnchor.MiddleCenter);
            ConfigureCenteredRect(
                teamSymbol.rectTransform,
                new Vector2(15f, -165f),
                new Vector2(70f, 70f));

            Text teamValue = ConfigureText(
                GetOrCreateUiChild(panel.transform, "TeamValue"),
                "-%",
                34,
                FontStyle.Bold,
                new Color(0.55f, 0.59f, 0.63f, 1f),
                TextAnchor.MiddleRight);
            ConfigureCenteredRect(
                teamValue.rectTransform,
                new Vector2(125f, -165f),
                new Vector2(130f, 60f));

            SanityHudUI hud = GetOrAddComponent<SanityHudUI>(canvasObject);
            SetObjectReference(hud, "_panel", panel);
            SetObjectReferenceArray(hud, "_playerLabelTexts", playerLabels);
            SetObjectReferenceArray(hud, "_playerSymbolTexts", playerSymbols);
            SetObjectReferenceArray(hud, "_playerValueTexts", playerValues);
            SetObjectReference(hud, "_teamLabelText", teamLabel);
            SetObjectReference(hud, "_teamSymbolText", teamSymbol);
            SetObjectReference(hud, "_teamValueText", teamValue);

            EditorUtility.SetDirty(canvasObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ValidateWorldMonitor(Scene scene)
        {
            if (FindSceneRoot(scene, LegacyHudCanvasObjectName) != null)
                throw new InvalidOperationException("기존 화면 고정 정신력 HUD가 남아 있습니다.");

            GameObject canvasObject = FindSceneRoot(scene, MonitorObjectName);
            SanityHudUI hud = canvasObject != null
                ? canvasObject.GetComponent<SanityHudUI>()
                : null;
            Canvas canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
            CanvasScaler scaler = canvasObject != null
                ? canvasObject.GetComponent<CanvasScaler>()
                : null;
            if (hud == null || canvas == null || scaler == null)
                throw new MissingReferenceException("Game 씬의 World Space 정신력 모니터가 없습니다.");
            if (canvas.renderMode != RenderMode.WorldSpace)
                throw new InvalidOperationException("정신력 모니터 Canvas는 World Space여야 합니다.");
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize)
            {
                throw new InvalidOperationException("정신력 모니터 CanvasScaler 기준이 잘못됐습니다.");
            }

            var serializedHud = new SerializedObject(hud);
            string[] requiredReferences =
            {
                "_panel",
                "_teamLabelText",
                "_teamSymbolText",
                "_teamValueText",
            };
            foreach (string propertyName in requiredReferences)
            {
                if (serializedHud.FindProperty(propertyName).objectReferenceValue == null)
                    throw new MissingReferenceException($"정신력 모니터의 {propertyName} 배선이 없습니다.");
            }

            string[] requiredArrays =
            {
                "_playerLabelTexts",
                "_playerSymbolTexts",
                "_playerValueTexts",
            };
            foreach (string propertyName in requiredArrays)
            {
                SerializedProperty array = serializedHud.FindProperty(propertyName);
                if (array == null || array.arraySize != 4)
                    throw new MissingReferenceException($"정신력 모니터의 {propertyName} 슬롯이 4개가 아닙니다.");

                for (int i = 0; i < array.arraySize; i++)
                {
                    if (array.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        throw new MissingReferenceException($"정신력 모니터의 {propertyName}[{i}] 배선이 없습니다.");
                }
            }

            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Contains("Gauge", StringComparison.Ordinal))
                    throw new InvalidOperationException("숫자 전용 정신력 모니터에 게이지 오브젝트가 남아 있습니다.");
            }

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            if (canvasRect.position != MonitorPosition
                || canvasRect.localScale != MonitorScale
                || canvasRect.sizeDelta != MonitorResolution)
            {
                throw new InvalidOperationException("정신력 모니터의 월드 위치·크기 설정이 잘못됐습니다.");
            }
        }

        private static GameObject FindSceneRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root;
            }

            return null;
        }

        private static GameObject GetOrCreateUiChild(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            if (existing != null)
            {
                if (existing is not RectTransform)
                    throw new InvalidOperationException($"{objectName}에는 RectTransform이 필요합니다.");

                return existing.gameObject;
            }

            var child = new GameObject(objectName, typeof(RectTransform));
            child.layer = LayerMask.NameToLayer("UI");
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void CreatePlayerSlot(
            Transform parent,
            int slotIndex,
            Vector2 anchoredPosition,
            out Text label,
            out Text symbol,
            out Text value)
        {
            GameObject slot = GetOrCreateUiChild(parent, $"PlayerSlot{slotIndex + 1}");
            ConfigureCenteredRect(
                slot.GetComponent<RectTransform>(),
                anchoredPosition,
                new Vector2(400f, 90f));

            Color disabledColor = new(0.55f, 0.59f, 0.63f, 1f);
            label = ConfigureText(
                GetOrCreateUiChild(slot.transform, "Label"),
                $"P{slotIndex + 1}",
                27,
                FontStyle.Bold,
                disabledColor,
                TextAnchor.MiddleRight);
            ConfigureCenteredRect(label.rectTransform, new Vector2(-125f, 0f), new Vector2(90f, 60f));

            symbol = ConfigureText(
                GetOrCreateUiChild(slot.transform, "Symbol"),
                "?",
                46,
                FontStyle.Normal,
                disabledColor,
                TextAnchor.MiddleCenter);
            ConfigureCenteredRect(symbol.rectTransform, new Vector2(-25f, 0f), new Vector2(70f, 70f));

            value = ConfigureText(
                GetOrCreateUiChild(slot.transform, "Value"),
                "-%",
                34,
                FontStyle.Bold,
                disabledColor,
                TextAnchor.MiddleRight);
            ConfigureCenteredRect(value.rectTransform, new Vector2(95f, 0f), new Vector2(130f, 60f));
        }

        private static void CreateFrameLine(
            Transform parent,
            string objectName,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject line = GetOrCreateUiChild(parent, objectName);
            ConfigureCenteredRect(line.GetComponent<RectTransform>(), anchoredPosition, size);
            ConfigureImage(line, new Color(0.28f, 0.34f, 0.4f, 1f));
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static Image ConfigureImage(GameObject target, Color color)
        {
            Image image = GetOrAddComponent<Image>(target);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text ConfigureText(
            GameObject target,
            string content,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment)
        {
            Text text = GetOrAddComponent<Text>(target);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureFixedRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            rect.localScale = Vector3.one;
        }

        private static void ConfigureCenteredRect(
            RectTransform rect,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            ConfigureFixedRect(
                rect,
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                Vector2.one * 0.5f,
                anchoredPosition,
                sizeDelta);
        }

        private static void FindSceneBindings(
            Scene scene,
            out GameInstaller installer,
            out SanityTeamService service)
        {
            installer = null;
            service = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (installer == null)
                    installer = root.GetComponentInChildren<GameInstaller>(true);
                if (service == null)
                    service = root.GetComponentInChildren<SanityTeamService>(true);
            }
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetObjectReferenceArray(
            Object target,
            string propertyName,
            Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
