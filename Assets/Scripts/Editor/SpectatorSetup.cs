using System;
using System.IO;
using GhostHunter.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 사망 후 관전(spectator-system.md, 사용자 확정 2026-09-12)을 Player 프리팹에 멱등으로
    /// 덧붙인다. <see cref="SpectatorController"/>와 전용 <c>SpectatorCamera</c>(Camera +
    /// AudioListener, 기본 비활성)를 추가·배선하고 관전 설정 에셋을 준비한다.
    ///
    /// <para>관전 전용 입력 액션(SpectateDescend·SpectateToggleMode·SpectateNext)은 이 도구가
    /// 만들지 않는다 — <see cref="MoleSkillSetup"/>·<see cref="QuickSlotSetup"/>과 같은 관례대로,
    /// 먼저 InputSystem_Actions.inputactions 에 있어야 하고 없으면 안내와 함께 실패한다.</para>
    /// </summary>
    public static class SpectatorSetup
    {
        internal const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        internal const string SettingsPath = "Assets/Settings/Gameplay/SpectatorSettings_Default.asset";
        internal const string SpectatorCameraObjectName = "SpectatorCamera";

        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        private static readonly string[] RequiredActions =
        {
            "Player/SpectateDescend",
            "Player/SpectateToggleMode",
            "Player/SpectateNext",
        };

        [MenuItem("GhostHunter/관전(사망 후) 설치", priority = 10)]
        public static void Install()
        {
            Install(true);
        }

        internal static void Install(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing spectator support.");

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                throw new MissingReferenceException($"{InputActionsPath} 를 찾지 못했습니다.");

            foreach (string actionPath in RequiredActions)
            {
                if (actions.FindAction(actionPath, false) == null)
                {
                    throw new InvalidOperationException(
                        $"{actionPath} 액션이 없습니다. InputSystem_Actions.inputactions 에 먼저 추가하세요 " +
                        "(SpectateDescend=Left Ctrl, SpectateToggleMode=V, SpectateNext=Mouse Right Button).");
                }
            }

            SpectatorSettings settings = LoadOrCreateAsset<SpectatorSettings>(SettingsPath);

            InstallPlayerPrefab(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateInstallation(settings);

            if (logCompletion)
            {
                Debug.Log(
                    "[SpectatorSetup] 관전 설치 완료.\n" +
                    "Player 프리팹의 SpectatorController와 SpectatorCamera(Camera+AudioListener, 기본 비활성)를 확인하세요.");
            }
        }

        internal static void ValidateInstallation(SpectatorSettings settings)
        {
            if (settings == null)
                throw new MissingReferenceException("관전 설정 에셋이 없습니다.");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
                throw new MissingReferenceException($"{PlayerPrefabPath} 를 찾지 못했습니다.");

            SpectatorController controller = playerPrefab.GetComponent<SpectatorController>();
            if (controller == null)
                throw new MissingComponentException("Player 프리팹에 SpectatorController 가 없습니다.");

            Transform cameraTransform = playerPrefab.transform.Find(SpectatorCameraObjectName);
            if (cameraTransform == null)
                throw new MissingReferenceException("Player 프리팹에 SpectatorCamera 자식이 없습니다.");

            Camera camera = cameraTransform.GetComponent<Camera>();
            AudioListener listener = cameraTransform.GetComponent<AudioListener>();
            if (camera == null || listener == null)
                throw new MissingComponentException("SpectatorCamera 에 Camera/AudioListener 가 없습니다.");

            if (camera.enabled || listener.enabled)
                throw new InvalidOperationException("SpectatorCamera 의 Camera/AudioListener 는 기본 비활성이어야 합니다.");

            if (cameraTransform.GetComponent<UniversalAdditionalCameraData>() == null)
                throw new MissingComponentException("SpectatorCamera 에 UniversalAdditionalCameraData 가 없습니다(URP).");

            PlayerInputReader input = playerPrefab.GetComponent<PlayerInputReader>();
            if (input == null)
                throw new MissingComponentException("Player 프리팹에 PlayerInputReader 가 없습니다.");

            SerializedObject serialized = new(controller);
            if (serialized.FindProperty("_settings").objectReferenceValue != settings)
                throw new InvalidOperationException("SpectatorController 설정 배선이 잘못됐습니다.");
            if (serialized.FindProperty("_input").objectReferenceValue != input)
                throw new InvalidOperationException("SpectatorController 입력 배선이 잘못됐습니다.");
            if (serialized.FindProperty("_spectatorCamera").objectReferenceValue != camera)
                throw new InvalidOperationException("SpectatorController 카메라 배선이 잘못됐습니다.");
            if (serialized.FindProperty("_spectatorAudioListener").objectReferenceValue != listener)
                throw new InvalidOperationException("SpectatorController 리스너 배선이 잘못됐습니다.");
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException($"설정 폴더가 없습니다: {folder}");

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void InstallPlayerPrefab(SpectatorSettings settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                PlayerInputReader input = root.GetComponent<PlayerInputReader>();
                if (input == null)
                    throw new MissingComponentException("Player 프리팹에 PlayerInputReader 가 없습니다.");

                bool changed = false;

                Transform cameraTransform = root.transform.Find(SpectatorCameraObjectName);
                GameObject cameraObject;
                if (cameraTransform == null)
                {
                    cameraObject = new GameObject(SpectatorCameraObjectName);
                    cameraObject.transform.SetParent(root.transform, false);
                    changed = true;
                }
                else
                {
                    cameraObject = cameraTransform.gameObject;
                }

                Camera camera = cameraObject.GetComponent<Camera>();
                if (camera == null)
                {
                    camera = cameraObject.AddComponent<Camera>();
                    changed = true;
                }
                if (camera.enabled)
                {
                    camera.enabled = false;
                    changed = true;
                }

                if (cameraObject.GetComponent<UniversalAdditionalCameraData>() == null)
                {
                    cameraObject.AddComponent<UniversalAdditionalCameraData>();
                    changed = true;
                }

                AudioListener listener = cameraObject.GetComponent<AudioListener>();
                if (listener == null)
                {
                    listener = cameraObject.AddComponent<AudioListener>();
                    changed = true;
                }
                if (listener.enabled)
                {
                    listener.enabled = false;
                    changed = true;
                }

                SpectatorController controller = root.GetComponent<SpectatorController>();
                if (controller == null)
                {
                    controller = root.AddComponent<SpectatorController>();
                    changed = true;
                }

                SerializedObject serialized = new(controller);
                bool wired = false;
                wired |= SetIfDifferent(serialized, "_settings", settings);
                wired |= SetIfDifferent(serialized, "_input", input);
                wired |= SetIfDifferent(serialized, "_spectatorCamera", camera);
                wired |= SetIfDifferent(serialized, "_spectatorAudioListener", listener);
                if (wired)
                {
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

        private static bool SetIfDifferent(SerializedObject serialized, string propertyName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(serialized.targetObject.GetType().Name, propertyName);

            if (property.objectReferenceValue == value)
                return false;

            property.objectReferenceValue = value;
            return true;
        }
    }
}
