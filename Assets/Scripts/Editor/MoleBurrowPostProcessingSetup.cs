using System;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 굴착 스킬 중 켜지는 화면 비네트를 설치한다 — Volume 프로필과 Game 씬의 Global Volume.
    /// 플레이어 카메라의 포스트 프로세싱 활성화는 <see cref="SanityPostProcessingSetup"/> 가 이미
    /// 설치해 뒀다면 그대로 재사용한다(멱등 — 꺼져 있으면 이 도구도 마저 켠다).
    ///
    /// 정신력 카메라 노이즈와 **별도의 프로필·오브젝트**를 쓴다 — 두 연출이 동시에 걸려도
    /// 서로 밀어내지 않고 단순히 겹쳐서 더 어두워질 뿐이다.
    /// </summary>
    public static class MoleBurrowPostProcessingSetup
    {
        internal const string ProfilePath =
            "Assets/Settings/PostProcessing/PP_MoleBurrowVignette.asset";
        internal const string VolumeObjectName = "MoleBurrowVignette";

        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string SettingsFolder = "Assets/Settings";
        private const string PostProcessingFolderName = "PostProcessing";

        [MenuItem("GhostHunter/두더지 굴착 카메라 연출 설치", priority = 7)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Exit Play Mode before installing the mole burrow camera post processing.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the mole burrow camera post processing. " +
                    $"The active scene is '{scene.path}'.");
            }

            VolumeProfile profile = LoadOrCreateProfile();
            InstallPlayerCamera();
            InstallSceneVolume(scene, profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateInstallation();

            if (logCompletion)
            {
                Debug.Log(
                    "[MoleBurrowPostProcessingSetup] 굴착 카메라 비네트 설치 완료 — " +
                    "Volume 프로필·Game 씬 Global Volume·Player 카메라 포스트 프로세싱.");
            }
        }

        internal static void ValidateInstallation()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
                throw new MissingReferenceException($"Volume 프로필이 없습니다: {ProfilePath}");

            if (!profile.TryGet(out Vignette vignette))
                throw new InvalidOperationException("Volume 프로필에 Vignette 오버라이드가 빠졌습니다.");

            // components 리스트에 있다고 안심할 수 없다 — AddObjectToAsset 를 빠뜨리면 리스트에는
            // 남아 있어도 디스크에는 저장되지 않아 다음 도메인 리로드에서 사라진다.
            if (!AssetDatabase.Contains(vignette))
            {
                throw new InvalidOperationException(
                    "Vignette 가 서브 에셋으로 저장되지 않았습니다 — AssetDatabase.AddObjectToAsset " +
                    "누락. 지금은 인스펙터에 보여도 다음 컴파일·에디터 재시작 후 오버라이드가 사라진다.");
            }

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (player == null)
                throw new MissingReferenceException($"Player 프리팹이 없습니다: {PlayerPrefabPath}");

            Camera camera = player.GetComponentInChildren<Camera>(true);
            if (camera == null)
                throw new MissingComponentException("Player 프리팹에 카메라가 없습니다.");

            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null || !cameraData.renderPostProcessing)
            {
                throw new InvalidOperationException(
                    "Player 카메라의 Post Processing 이 꺼져 있습니다. Volume 이 화면에 적용되지 않습니다.");
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                return;

            GameObject volumeObject = FindSceneRoot(scene, VolumeObjectName);
            if (volumeObject == null)
                throw new MissingReferenceException("Game 씬에 굴착 카메라 Volume 이 없습니다.");

            var volume = volumeObject.GetComponent<Volume>();
            var driver = volumeObject.GetComponent<MoleBurrowCameraEffect>();
            if (volume == null || driver == null)
                throw new MissingComponentException("굴착 카메라 Volume 배선이 빠졌습니다.");

            if (!volume.isGlobal || volume.sharedProfile != profile)
                throw new InvalidOperationException("굴착 카메라 Volume 설정이 잘못됐습니다.");

            if (!Mathf.Approximately(volume.weight, 0f))
            {
                throw new InvalidOperationException(
                    "굴착 카메라 Volume 의 저장된 가중치는 0이어야 합니다. " +
                    "0이 아니면 에디터에서 늘 비네트가 낀 화면으로 보인다.");
            }
        }

        private static VolumeProfile LoadOrCreateProfile()
        {
            if (!AssetDatabase.IsValidFolder($"{SettingsFolder}/{PostProcessingFolderName}"))
                AssetDatabase.CreateFolder(SettingsFolder, PostProcessingFolderName);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // 정신력 노이즈보다 더 어둡고 좁게 — "거의 땅속에 파묻힌" 느낌만 준다. 그레인·수차는
            // 넣지 않는다(정신력 연출과 겹쳐도 서로 구분되게, §6 연출은 여전히 TBD 라 최소로 둔다).
            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.active = true;
            Override(vignette.color, new Color(0.02f, 0.015f, 0.01f));
            Override(vignette.intensity, 0.65f);
            Override(vignette.smoothness, 0.3f);
            Override(vignette.rounded, false);
            EditorUtility.SetDirty(vignette);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void InstallPlayerCamera()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Camera camera = root.GetComponentInChildren<Camera>(true);
                if (camera == null)
                    throw new MissingComponentException("Player 프리팹에 카메라가 없습니다.");

                var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData == null)
                    cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();

                if (cameraData.renderPostProcessing)
                    return;

                cameraData.renderPostProcessing = true;
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void InstallSceneVolume(Scene scene, VolumeProfile profile)
        {
            GameObject volumeObject = FindSceneRoot(scene, VolumeObjectName);
            if (volumeObject == null)
            {
                volumeObject = new GameObject(VolumeObjectName);
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
            }

            var volume = volumeObject.GetComponent<Volume>();
            if (volume == null)
                volume = volumeObject.AddComponent<Volume>();

            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            // 저장된 가중치는 반드시 0이다. 런타임에 MoleBurrowCameraEffect 가 올린다.
            volume.weight = 0f;

            var driver = volumeObject.GetComponent<MoleBurrowCameraEffect>();
            if (driver == null)
                driver = volumeObject.AddComponent<MoleBurrowCameraEffect>();

            var serialized = new SerializedObject(driver);
            serialized.FindProperty("_volume").objectReferenceValue = volume;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// <see cref="VolumeProfile.Add{T}"/> 는 컴포넌트를 <c>components</c> 리스트에 넣기만 할 뿐,
        /// 그 자체로는 에셋 파일에 저장되지 않는다 — 프로필과 별개의 서브 에셋이라
        /// <see cref="AssetDatabase.AddObjectToAsset"/> 을 직접 불러야 <c>SaveAssets</c> 때 같이
        /// 기록된다. 이걸 빠뜨리면 컴포넌트가 인스펙터에 "추가된 것처럼" 보이다가 다음 도메인
        /// 리로드(컴파일·에디터 재시작)에서 조용히 사라진다 — 오버라이드가 하나도 안 걸린 것처럼 보이는 원인.
        /// </summary>
        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing))
                return existing;

            T component = profile.Add<T>();
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void Override<T>(VolumeParameter<T> parameter, T value)
        {
            parameter.overrideState = true;
            parameter.value = value;
        }

        private static GameObject FindSceneRoot(Scene scene, string objectName)
        {
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == objectName)
                    return candidate;
            }

            return null;
        }
    }
}
