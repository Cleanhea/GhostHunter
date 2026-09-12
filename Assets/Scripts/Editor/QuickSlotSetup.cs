using System;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 퀵슬롯 휠 UI를 Game 씬에 멱등으로 덧붙인다. 기존 Game 씬을 재생성하지 않으며, 더미
    /// 아이템 2종 + 로드아웃(4슬롯 중 2개만 채움) + UI 설정 에셋을 함께 준비한다.
    /// </summary>
    public static class QuickSlotSetup
    {
        internal const string ScenePath = "Assets/Scenes/Game.unity";
        internal const string HudObjectName = "PrototypeUI";
        internal const string UiSettingsPath = "Assets/Settings/Gameplay/QuickSlotUiSettings_Default.asset";
        internal const string LoadoutPath = "Assets/Settings/Gameplay/QuickSlotLoadout_Default.asset";
        internal const string Item1Path = "Assets/Settings/Gameplay/QuickSlotItem_Placeholder1.asset";
        internal const string Item2Path = "Assets/Settings/Gameplay/QuickSlotItem_Placeholder2.asset";

        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("GhostHunter/퀵슬롯 HUD 설치", priority = 9)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the quick slot wheel.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the quick slot wheel. " +
                    $"The active scene is '{scene.path}'.");
            }

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                throw new MissingReferenceException($"{InputActionsPath} 를 찾지 못했습니다.");

            if (actions.FindAction("Player/QuickSlot", false) == null)
            {
                throw new InvalidOperationException(
                    "Player/QuickSlot 액션이 없습니다. InputSystem_Actions.inputactions 를 먼저 갱신하세요.");
            }

            QuickSlotUiSettings uiSettings = LoadOrCreateAsset<QuickSlotUiSettings>(UiSettingsPath);
            QuickSlotItemDefinition item1 = LoadOrCreatePlaceholderItem(
                Item1Path, "id1", "더미 아이템 1", "인벤토리 시스템이 붙기 전까지의 표시용 더미 데이터입니다.",
                new Color32(230, 140, 40, 255));
            QuickSlotItemDefinition item2 = LoadOrCreatePlaceholderItem(
                Item2Path, "id2", "더미 아이템 2", "인벤토리 시스템이 붙기 전까지의 표시용 더미 데이터입니다.",
                new Color32(60, 140, 220, 255));
            QuickSlotLoadout loadout = LoadOrCreateLoadout(LoadoutPath, item1, item2);

            InstallHud(scene, uiSettings, loadout);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateInstallation(scene, uiSettings, loadout);

            if (logCompletion)
            {
                Debug.Log(
                    "[QuickSlotSetup] 퀵슬롯 휠 UI 설치 완료.\n" +
                    "Player/QuickSlot(Tab), PrototypeUI의 QuickSlotWheelUi, 더미 로드아웃(2/4 슬롯 채움)을 확인하세요.");
            }
        }

        internal static void ValidateInstallation(
            Scene scene,
            QuickSlotUiSettings uiSettings,
            QuickSlotLoadout loadout)
        {
            if (uiSettings == null || loadout == null)
                throw new MissingReferenceException("퀵슬롯 UI 설정 또는 로드아웃 에셋이 없습니다.");

            GameObject hudObject = FindSceneObject(scene, HudObjectName);
            QuickSlotWheelUi wheel = hudObject != null ? hudObject.GetComponent<QuickSlotWheelUi>() : null;
            if (wheel == null)
                throw new MissingComponentException("Game 씬 PrototypeUI 에 QuickSlotWheelUi 가 없습니다.");

            SerializedObject wheelSerialized = new(wheel);
            if (wheelSerialized.FindProperty("_uiSettings").objectReferenceValue != uiSettings)
                throw new InvalidOperationException("QuickSlotWheelUi UI 설정 배선이 잘못됐습니다.");

            if (wheelSerialized.FindProperty("_loadout").objectReferenceValue != loadout)
                throw new InvalidOperationException("QuickSlotWheelUi 로드아웃 배선이 잘못됐습니다.");
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

        private static QuickSlotItemDefinition LoadOrCreatePlaceholderItem(
            string path, string id, string displayName, string description, Color placeholderColor)
        {
            QuickSlotItemDefinition existing = AssetDatabase.LoadAssetAtPath<QuickSlotItemDefinition>(path);
            if (existing != null)
                return existing;

            QuickSlotItemDefinition item = LoadOrCreateAsset<QuickSlotItemDefinition>(path);
            SerializedObject serialized = new(item);
            serialized.FindProperty("_id").stringValue = id;
            serialized.FindProperty("_displayName").stringValue = displayName;
            serialized.FindProperty("_description").stringValue = description;
            serialized.FindProperty("_placeholderColor").colorValue = placeholderColor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return item;
        }

        private static QuickSlotLoadout LoadOrCreateLoadout(
            string path, QuickSlotItemDefinition item1, QuickSlotItemDefinition item2)
        {
            QuickSlotLoadout existing = AssetDatabase.LoadAssetAtPath<QuickSlotLoadout>(path);
            if (existing != null)
                return existing;

            QuickSlotLoadout loadout = LoadOrCreateAsset<QuickSlotLoadout>(path);
            SerializedObject serialized = new(loadout);
            SerializedProperty slots = serialized.FindProperty("_slots");
            slots.arraySize = 4;
            slots.GetArrayElementAtIndex(0).objectReferenceValue = item1;
            slots.GetArrayElementAtIndex(1).objectReferenceValue = null;
            slots.GetArrayElementAtIndex(2).objectReferenceValue = item2;
            slots.GetArrayElementAtIndex(3).objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loadout);
            return loadout;
        }

        private static void InstallHud(Scene scene, QuickSlotUiSettings uiSettings, QuickSlotLoadout loadout)
        {
            GameObject hudObject = FindSceneObject(scene, HudObjectName);
            if (hudObject == null)
            {
                hudObject = new GameObject(HudObjectName);
                SceneManager.MoveGameObjectToScene(hudObject, scene);
            }

            QuickSlotWheelUi wheel = hudObject.GetComponent<QuickSlotWheelUi>();
            if (wheel == null)
                wheel = hudObject.AddComponent<QuickSlotWheelUi>();

            SetObjectReference(wheel, "_uiSettings", uiSettings);
            SetObjectReference(wheel, "_loadout", loadout);
            EditorUtility.SetDirty(wheel);
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

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
