using System;
using System.Linq;
using System.Reflection;
using GhostHunter.Data;
using GhostHunter.DebugTools;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Voice;
using GhostHunter.Systems.Installers;
using GhostHunter.Systems.Steam;
using GhostHunter.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>음성 설정·플레이어·Bootstrap/Game 서비스 배선을 재현하고 검증한다.</summary>
    public static class VoiceChatSetup
    {
        public const string SettingsPath = "Assets/Settings/Gameplay/VoiceChatSettings.asset";
        private const string PlayerPath = "Assets/Prefabs/Player.prefab";
        private const string MixerPath = "Assets/Settings/VoiceMixer.mixer";
        [MenuItem("GhostHunter/음성 채팅 설치", priority = 12)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play 모드를 종료한 뒤 실행하세요.");
            Scene previous = SceneManager.GetActiveScene();
            Scene bootstrap = Open("Assets/Scenes/Bootstrap.unity", out bool closeBootstrap);
            Scene game = Open("Assets/Scenes/Game.unity", out bool closeGame);
            try
            {
                VoiceChatSettings settings = AssetDatabase.LoadAssetAtPath<VoiceChatSettings>(SettingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<VoiceChatSettings>();
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }
                Set(settings, "_outputGroup", EnsureMixer());
                InstallPlayer(settings);
                BootstrapInstaller bootstrapInstaller = Find<BootstrapInstaller>(bootstrap);
                SteamVoiceCapture capture = bootstrapInstaller.GetComponent<SteamVoiceCapture>();
                if (capture == null) capture = Undo.AddComponent<SteamVoiceCapture>(bootstrapInstaller.gameObject);
                Set(bootstrapInstaller, "_voiceCapture", capture);
                GameInstaller gameInstaller = Find<GameInstaller>(game);
                Set(gameInstaller, "_voiceSettings", settings);
                if (gameInstaller.GetComponent<VoiceIndicatorHud>() == null) Undo.AddComponent<VoiceIndicatorHud>(gameInstaller.gameObject);
                if (gameInstaller.GetComponent<VoiceDebugHud>() == null) Undo.AddComponent<VoiceDebugHud>(gameInstaller.gameObject);
                EditorSceneManager.MarkSceneDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(game);
                if (!EditorSceneManager.SaveScene(bootstrap) || !EditorSceneManager.SaveScene(game))
                    throw new InvalidOperationException("음성 씬 저장 실패");
                AssetDatabase.SaveAssets();
                ValidateInstallation();
                Debug.Log("[VoiceChatSetup] 음성 프리팹·설정·믹서·Bootstrap/Game 저장 및 검증 완료", settings);
            }
            finally
            {
                if (closeGame) EditorSceneManager.CloseScene(game, true);
                if (closeBootstrap) EditorSceneManager.CloseScene(bootstrap, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        public static void ValidateInstallation()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var emitter = player.GetComponent<PlayerVoiceEmitter>();
            if (emitter == null) throw new MissingComponentException("음성 송신 컴포넌트 누락");
            SerializedObject serialized = new(emitter);
            foreach (string field in new[] { "_settings", "_input", "_look", "_motor", "_sanity", "_receiver" })
                if (serialized.FindProperty(field).objectReferenceValue == null) throw new MissingReferenceException(field);
            var identity = new SerializedObject(player.GetComponent<NetworkObject>());
            if (identity.FindProperty("GlobalObjectIdHash").uintValue == 0) throw new InvalidOperationException("Player 해시가 0입니다.");
            var settings = AssetDatabase.LoadAssetAtPath<VoiceChatSettings>(SettingsPath);
            if (settings == null || settings.OutputGroup == null) throw new MissingReferenceException("음성 믹서 연결 누락");
        }
        private static void InstallPlayer(VoiceChatSettings settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                PlayerVoiceEmitter emitter = root.GetComponent<PlayerVoiceEmitter>();
                if (emitter == null) emitter = root.AddComponent<PlayerVoiceEmitter>();
                Transform voice = root.transform.Find("VoiceAudio");
                if (voice == null)
                {
                    voice = new GameObject("VoiceAudio").transform;
                    voice.SetParent(root.transform, false);
                }
                AudioSource source = voice.GetComponent<AudioSource>();
                if (source == null) source = voice.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                AudioLowPassFilter filter = voice.GetComponent<AudioLowPassFilter>();
                if (filter == null) filter = voice.gameObject.AddComponent<AudioLowPassFilter>();
                VoiceReceiver receiver = voice.GetComponent<VoiceReceiver>();
                if (receiver == null) receiver = voice.gameObject.AddComponent<VoiceReceiver>();
                Set(receiver, "_source", source);
                Set(receiver, "_filter", filter);
                Set(emitter, "_settings", settings);
                Set(emitter, "_input", root.GetComponent<PlayerInputReader>());
                Set(emitter, "_look", root.GetComponent<PlayerLook>());
                Set(emitter, "_motor", root.GetComponent<PlayerMotor>());
                Set(emitter, "_sanity", root.GetComponent<SanityNetworkState>());
                Set(emitter, "_receiver", receiver);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.ImportAsset(PlayerPath, ImportAssetOptions.ForceUpdate);
        }
        private static AudioMixerGroup EnsureMixer()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            // AudioMixer 에셋 생성은 Unity의 에디터 API에만 있어 버전 한정 리플렉션으로 호출한다.
            Type controller = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController", true);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            if (mixer == null) mixer = (AudioMixer)controller.GetMethod("CreateMixerControllerAtPath", flags).Invoke(null, new object[] { MixerPath });
            Object master = (Object)controller.GetProperty("masterGroup", flags).GetValue(mixer);
            var serializedMaster = new SerializedObject(master);
            SerializedProperty children = serializedMaster.FindProperty("m_Children");
            foreach (string name in new[] { "SFX", "Voice" })
            {
                if (mixer.FindMatchingGroups(name).Length != 0) continue;
                Object group = (Object)controller.GetMethod("CreateNewGroup", flags).Invoke(mixer, new object[] { name, true });
                children.InsertArrayElementAtIndex(children.arraySize);
                children.GetArrayElementAtIndex(children.arraySize - 1).objectReferenceValue = group;
                serializedMaster.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(mixer);
            return mixer.FindMatchingGroups("Voice").Single();
        }
        private static Scene Open(string path, out bool opened)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            else if (scene.isDirty) throw new InvalidOperationException($"먼저 기존 변경을 저장하세요: {path}");
            return scene;
        }
        private static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
