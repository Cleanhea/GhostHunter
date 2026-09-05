using System;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 탐지 스킬과 공통 스킬 UI를 Game 씬·Player 프리팹에 멱등으로 덧붙인다.
    /// 아이콘 임포트 설정도 Unity API로 고치며, 기존 Game 씬을 재생성하지 않는다.
    /// </summary>
    public static class MoleSkillSetup
    {
        internal const string ScenePath = "Assets/Scenes/Game.unity";
        internal const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        internal const string DetectionSettingsPath =
            "Assets/Settings/Gameplay/DetectionSkillSettings_Default.asset";
        internal const string UiSettingsPath =
            "Assets/Settings/Gameplay/MoleSkillUiSettings_Default.asset";
        internal const string HudObjectName = "PrototypeUI";
        internal const string TemporaryRootName = "DetectionSanityTargets_[TEMP]";

        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string SkillIconFolder = "Assets/Sprite/Skill_icon";
        private const string DetectionTargetName = "[TEMP] DetectionTarget_MovingFurniture";
        private const string StainTargetName = "[TEMP] DetectionTarget_Stain";

        private static readonly string[] SkillIconPaths =
        {
            $"{SkillIconFolder}/ICON_ detection_on.png",
            $"{SkillIconFolder}/ICON_ detection_off.png",
            $"{SkillIconFolder}/ICON_excavation_on.png",
            $"{SkillIconFolder}/ICON_excavation_off.png",
        };

        [MenuItem("GhostHunter/두더지 스킬(탐지·공통 UI) 설치", priority = 8)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before installing the mole skill and detection UI.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the mole skill setup. " +
                    $"The active scene is '{scene.path}'.");
            }

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                throw new MissingReferenceException($"{InputActionsPath} 를 찾지 못했습니다.");

            if (actions.FindAction("Player/Detect", false) == null)
            {
                throw new InvalidOperationException(
                    "Player/Detect 액션이 없습니다. InputSystem_Actions.inputactions 를 먼저 갱신하세요.");
            }

            DetectionSkillSettings detectionSettings = LoadOrCreateAsset<DetectionSkillSettings>(
                DetectionSettingsPath);
            MoleSkillUiSettings uiSettings = LoadOrCreateAsset<MoleSkillUiSettings>(UiSettingsPath);

            // 기존 설정 에셋도 새 [SerializeField] 시전 연출 값을 저장하도록 한 번 직렬화한다.
            EditorUtility.SetDirty(detectionSettings);

            FixSkillIconImporters();
            InstallPlayerPrefab(detectionSettings);
            InstallHud(scene, uiSettings);
            InstallTemporaryMarkers(scene);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateInstallation(scene, detectionSettings, uiSettings);

            if (logCompletion)
            {
                Debug.Log(
                    "[MoleSkillSetup] 탐지 스킬·공통 스킬 UI 설치 완료.\n" +
                    "Player/Detect(Q), DetectionSkillController, 우측 상단 UI, " +
                    "아이콘 Single 임포트, [TEMP] 마커를 확인하세요.");
            }
        }

        internal static void ValidateInstallation(
            Scene scene,
            DetectionSkillSettings detectionSettings,
            MoleSkillUiSettings uiSettings)
        {
            if (detectionSettings == null || uiSettings == null)
                throw new MissingReferenceException("탐지·UI 설정 에셋이 없습니다.");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
                throw new MissingReferenceException($"{PlayerPrefabPath} 를 찾지 못했습니다.");

            DetectionSkillController detection = playerPrefab.GetComponent<DetectionSkillController>();
            if (detection == null)
                throw new MissingComponentException("Player 프리팹에 DetectionSkillController 가 없습니다.");

            SerializedObject detectionSerialized = new(detection);
            if (detectionSerialized.FindProperty("_settings").objectReferenceValue != detectionSettings)
                throw new InvalidOperationException("DetectionSkillController 설정 배선이 잘못됐습니다.");

            foreach (string iconPath in SkillIconPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                if (importer == null || importer.spriteImportMode != SpriteImportMode.Single)
                    throw new InvalidOperationException($"아이콘 임포트 설정이 Single이 아닙니다: {iconPath}");

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (sprite == null)
                    throw new InvalidOperationException($"아이콘 Sprite 서브 에셋을 읽지 못했습니다: {iconPath}");
            }

            GameObject hudObject = FindSceneObject(scene, HudObjectName);
            MoleSkillHud hud = hudObject != null
                ? hudObject.GetComponent<MoleSkillHud>()
                : null;
            if (hud == null)
                throw new MissingComponentException("Game 씬 PrototypeUI 에 MoleSkillHud 가 없습니다.");

            SerializedObject hudSerialized = new(hud);
            Sprite[] expectedIcons =
            {
                AssetDatabase.LoadAssetAtPath<Sprite>(SkillIconPaths[0]),
                AssetDatabase.LoadAssetAtPath<Sprite>(SkillIconPaths[1]),
                AssetDatabase.LoadAssetAtPath<Sprite>(SkillIconPaths[2]),
                AssetDatabase.LoadAssetAtPath<Sprite>(SkillIconPaths[3]),
            };
            string[] iconFields =
            {
                "_detectionOnIcon",
                "_detectionOffIcon",
                "_excavationOnIcon",
                "_excavationOffIcon",
            };
            for (int i = 0; i < iconFields.Length; i++)
            {
                if (hudSerialized.FindProperty(iconFields[i]).objectReferenceValue != expectedIcons[i])
                    throw new InvalidOperationException(
                        $"MoleSkillHud {iconFields[i]} icon wiring is incorrect.");
            }

            if (hudSerialized.FindProperty("_uiSettings").objectReferenceValue != uiSettings)
                throw new InvalidOperationException("MoleSkillHud UI 설정 배선이 잘못됐습니다.");

            if (FindSceneObject(scene, TemporaryRootName) == null)
                throw new MissingReferenceException("탐지 임시 마커 루트가 없습니다.");
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException($"설정 폴더가 없습니다: {folder}");

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void FixSkillIconImporters()
        {
            foreach (string iconPath in SkillIconPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
                if (importer == null)
                    throw new MissingReferenceException($"아이콘 TextureImporter가 없습니다: {iconPath}");

                bool changed = importer.textureType != TextureImporterType.Sprite
                    || importer.spriteImportMode != SpriteImportMode.Single
                    || importer.mipmapEnabled
                    || importer.isReadable;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.isReadable = false;

                if (changed)
                    importer.SaveAndReimport();
                else
                    AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceUpdate);
            }
        }

        private static void InstallPlayerPrefab(DetectionSkillSettings settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                DetectionSkillController controller = root.GetComponent<DetectionSkillController>();
                bool changed = controller == null;
                if (controller == null)
                    controller = root.AddComponent<DetectionSkillController>();

                SerializedObject serialized = new(controller);
                SerializedProperty property = serialized.FindProperty("_settings");
                if (property.objectReferenceValue != settings)
                {
                    property.objectReferenceValue = settings;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void InstallHud(Scene scene, MoleSkillUiSettings uiSettings)
        {
            GameObject hudObject = FindSceneObject(scene, HudObjectName);
            if (hudObject == null)
            {
                hudObject = new GameObject(HudObjectName);
                SceneManager.MoveGameObjectToScene(hudObject, scene);
            }

            MoleSkillHud hud = hudObject.GetComponent<MoleSkillHud>();
            if (hud == null)
                hud = hudObject.AddComponent<MoleSkillHud>();

            Sprite detectionOn = LoadSprite(SkillIconPaths[0]);
            Sprite detectionOff = LoadSprite(SkillIconPaths[1]);
            Sprite excavationOn = LoadSprite(SkillIconPaths[2]);
            Sprite excavationOff = LoadSprite(SkillIconPaths[3]);

            SetObjectReference(hud, "_uiSettings", uiSettings);
            SetObjectReference(hud, "_detectionOnIcon", detectionOn);
            SetObjectReference(hud, "_detectionOffIcon", detectionOff);
            SetObjectReference(hud, "_excavationOnIcon", excavationOn);
            SetObjectReference(hud, "_excavationOffIcon", excavationOff);
            EditorUtility.SetDirty(hud);
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                throw new MissingReferenceException($"Sprite 서브 에셋이 없습니다: {path}");

            return sprite;
        }

        private static void InstallTemporaryMarkers(Scene scene)
        {
            GameObject root = FindSceneObject(scene, TemporaryRootName);
            if (root == null)
            {
                root = new GameObject(TemporaryRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            FurnitureGrabTarget[] furniture = Object.FindObjectsByType<FurnitureGrabTarget>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Array.Sort(furniture, CompareByName);

            if (furniture.Length > 0)
            {
                DetectionTargetMarker marker = furniture[0].GetComponent<DetectionTargetMarker>();
                if (marker == null)
                    marker = furniture[0].gameObject.AddComponent<DetectionTargetMarker>();

                SetEnumValue(marker, "_kind", DetectionTargetKind.MovingFurniture);
                SetBoolValue(marker, "_targetActive", true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            }

            GameObject stain = FindChild(root.transform, StainTargetName);
            if (stain == null)
            {
                stain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stain.name = StainTargetName;
                stain.transform.SetParent(root.transform, false);
                stain.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                stain.transform.localScale = new Vector3(0.65f, 0.02f, 0.65f);
            }

            Collider stainCollider = stain.GetComponent<Collider>();
            if (stainCollider != null)
                stainCollider.enabled = false;

            DetectionTargetMarker stainMarker = stain.GetComponent<DetectionTargetMarker>();
            if (stainMarker == null)
                stainMarker = stain.AddComponent<DetectionTargetMarker>();

            SetEnumValue(stainMarker, "_kind", DetectionTargetKind.Stain);
            SetBoolValue(stainMarker, "_targetActive", true);
            EditorUtility.SetDirty(stainMarker);
            EditorUtility.SetDirty(root);
        }

        private static int CompareByName(FurnitureGrabTarget left, FurnitureGrabTarget right)
        {
            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        private static GameObject FindChild(Transform parent, string objectName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == objectName)
                    return child.gameObject;
            }

            return null;
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root;

                Transform child = FindDescendant(root.transform, objectName);
                if (child != null)
                    return child.gameObject;
            }

            return null;
        }

        private static Transform FindDescendant(Transform parent, string objectName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == objectName)
                    return child;

                Transform nested = FindDescendant(child, objectName);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnumValue(Object target, string propertyName, Enum value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.enumValueIndex = Convert.ToInt32(value);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBoolValue(Object target, string propertyName, bool value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
