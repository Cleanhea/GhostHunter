using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GhostHunter.Core.Scenes;
using GhostHunter.Data.Scenes;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace GhostHunter.Systems.SceneFlow
{
    /// <summary>
    /// 씬 전환을 수행한다. Bootstrap 은 그대로 두고 게임플레이 씬만 additive 로 갈아 끼운다.
    /// 네트워크 세션 중에는 NGO 씬 매니저로, 그 외에는 로컬 로드로 처리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneFlowController : MonoBehaviour, ISceneFlow
    {
        [SerializeField] private SceneNameSO _scenes;

        [Tooltip("Bootstrap 이 뜬 직후 자동으로 올릴 씬.")]
        [SerializeField] private SceneId _firstScene = SceneId.Title;

        private Scene _currentScene;
        private bool _currentLoadedByNgo;

        public SceneId Current { get; private set; } = SceneId.Bootstrap;
        public bool IsLoading { get; private set; }
        public event Action<SceneId> SceneChanged;

        private void Start()
        {
            if (_scenes == null)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: SceneNameSO 미할당", this);
                enabled = false;
                return;
            }

            // 에디터에서 Bootstrap 과 작업 중인 씬을 함께 열어둔 경우, 그 씬을 Title 로 덮지 않는다.
            if (SceneManager.sceneCount > 1)
            {
                AdoptAlreadyLoadedScene();
                return;
            }

            Load(_firstScene);
        }

        public void Load(SceneId scene)
        {
            if (!enabled)
                return;

            if (IsLoading)
            {
                Debug.LogWarning($"{nameof(SceneFlowController)}: 전환 중이라 {scene} 요청을 무시한다.", this);
                return;
            }

            if (scene == SceneId.Bootstrap)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: Bootstrap 은 전환 대상이 아니다.", this);
                return;
            }

            if (scene == Current)
            {
                Debug.LogWarning($"{nameof(SceneFlowController)}: 이미 {scene} 이다.", this);
                return;
            }

            LoadAsync(scene).Forget();
        }

        private async UniTaskVoid LoadAsync(SceneId target)
        {
            string targetName = _scenes.GetSceneName(target);

            if (string.IsNullOrEmpty(targetName))
            {
                Debug.LogError(
                    $"{nameof(SceneFlowController)}: {target} 의 씬이 SceneNameSO 에 지정되지 않았다.", this);
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            bool useNgo = networkManager != null
                          && networkManager.IsListening
                          && networkManager.NetworkConfig.EnableSceneManagement;

            if (useNgo && !networkManager.IsServer)
            {
                // 클라이언트는 서버의 씬 이벤트를 따라갈 뿐 스스로 전환을 시작할 수 없다.
                Debug.LogWarning(
                    $"{nameof(SceneFlowController)}: 세션 중 클라이언트는 씬 전환을 시작할 수 없다. ({target})",
                    this);
                return;
            }

            IsLoading = true;

            Scene previousScene = _currentScene;
            bool previousLoadedByNgo = _currentLoadedByNgo;
            List<Behaviour> suspended = SuspendSceneInput(previousScene);

            try
            {
                Scene loaded = useNgo
                    ? await LoadThroughNgoAsync(networkManager, targetName)
                    : await LoadLocallyAsync(targetName);

                if (!loaded.IsValid())
                {
                    RestoreSceneInput(suspended);
                    return;
                }

                SceneManager.SetActiveScene(loaded);

                _currentScene = loaded;
                _currentLoadedByNgo = useNgo;
                Current = target;

                if (previousScene.IsValid() && previousScene.isLoaded)
                    await UnloadAsync(networkManager, previousScene, previousLoadedByNgo);

                SceneChanged?.Invoke(target);
            }
            catch (OperationCanceledException)
            {
                // 전환 도중 앱이 내려갔다. 복구할 것이 없다.
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: {target} 전환 실패 — {e}", this);
                RestoreSceneInput(suspended);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async UniTask<Scene> LoadLocallyAsync(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation == null)
            {
                Debug.LogError(
                    $"{nameof(SceneFlowController)}: 씬 {sceneName} 로드를 시작하지 못했다. " +
                    "빌드 씬 목록을 확인할 것.", this);
                return default;
            }

            await operation.ToUniTask(cancellationToken: destroyCancellationToken);
            return SceneManager.GetSceneByName(sceneName);
        }

        private async UniTask<Scene> LoadThroughNgoAsync(NetworkManager networkManager, string sceneName)
        {
            var completion = new UniTaskCompletionSource();

            void OnLoadCompleted(
                string loadedName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
            {
                if (loadedName == sceneName)
                    completion.TrySetResult();
            }

            networkManager.SceneManager.OnLoadEventCompleted += OnLoadCompleted;

            try
            {
                SceneEventProgressStatus status =
                    networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);

                if (status != SceneEventProgressStatus.Started)
                {
                    Debug.LogError(
                        $"{nameof(SceneFlowController)}: NGO 씬 로드를 시작하지 못했다 — {status}", this);
                    return default;
                }

                await completion.Task.AttachExternalCancellation(destroyCancellationToken);
            }
            finally
            {
                networkManager.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
            }

            return SceneManager.GetSceneByName(sceneName);
        }

        /// <summary>올린 주체가 내린다. NGO 가 올린 씬을 로컬로 내리면 클라이언트와 어긋난다.</summary>
        private async UniTask UnloadAsync(NetworkManager networkManager, Scene scene, bool loadedByNgo)
        {
            if (!loadedByNgo)
            {
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

                if (operation != null)
                    await operation.ToUniTask(cancellationToken: destroyCancellationToken);

                return;
            }

            if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            {
                // 세션이 이미 끝났으면 NGO 가 씬을 추적하지 않는다. 로컬로 내리는 수밖에 없다.
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

                if (operation != null)
                    await operation.ToUniTask(cancellationToken: destroyCancellationToken);

                return;
            }

            string sceneName = scene.name;
            var completion = new UniTaskCompletionSource();

            void OnUnloadCompleted(
                string unloadedName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
            {
                if (unloadedName == sceneName)
                    completion.TrySetResult();
            }

            networkManager.SceneManager.OnUnloadEventCompleted += OnUnloadCompleted;

            try
            {
                SceneEventProgressStatus status = networkManager.SceneManager.UnloadScene(scene);

                if (status != SceneEventProgressStatus.Started)
                {
                    Debug.LogWarning(
                        $"{nameof(SceneFlowController)}: NGO 씬 언로드를 시작하지 못했다 — {status}", this);
                    return;
                }

                await completion.Task.AttachExternalCancellation(destroyCancellationToken);
            }
            finally
            {
                networkManager.SceneManager.OnUnloadEventCompleted -= OnUnloadCompleted;
            }
        }

        /// <summary>
        /// 전환 중에는 두 씬이 겹친다. 이전 씬의 EventSystem 과 AudioListener 를 먼저 끈다.
        /// 둘이 동시에 살아 있으면 입력이 갈리고 콘솔에 AudioListener 중복 경고가 뜬다.
        /// </summary>
        private static List<Behaviour> SuspendSceneInput(Scene scene)
        {
            var suspended = new List<Behaviour>();

            if (!scene.IsValid() || !scene.isLoaded)
                return suspended;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (EventSystem eventSystem in root.GetComponentsInChildren<EventSystem>(true))
                {
                    if (!eventSystem.enabled)
                        continue;

                    eventSystem.enabled = false;
                    suspended.Add(eventSystem);
                }

                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    if (!listener.enabled)
                        continue;

                    listener.enabled = false;
                    suspended.Add(listener);
                }
            }

            return suspended;
        }

        /// <summary>전환이 실패했을 때 이전 씬을 원상 복구한다.</summary>
        private static void RestoreSceneInput(List<Behaviour> suspended)
        {
            foreach (Behaviour behaviour in suspended)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
        }

        /// <summary>
        /// Bootstrap 외에 이미 올라와 있는 씬이 있으면 그것을 현재 씬으로 받아들인다.
        /// 에디터에서 Play From Bootstrap 을 끄고 특정 씬을 반복 수정할 때의 경로다.
        /// </summary>
        private void AdoptAlreadyLoadedScene()
        {
            string bootstrapName = _scenes.GetSceneName(SceneId.Bootstrap);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded || scene.name == bootstrapName)
                    continue;

                if (!_scenes.TryResolve(scene.name, out SceneId id))
                    continue;

                _currentScene = scene;
                _currentLoadedByNgo = false;
                Current = id;
                SceneManager.SetActiveScene(scene);

                Debug.Log(
                    $"{nameof(SceneFlowController)}: 이미 열려 있는 {id} 씬을 현재 씬으로 받아들였다. " +
                    "부팅 전환을 건너뛴다.", this);
                return;
            }

            Debug.LogWarning(
                $"{nameof(SceneFlowController)}: 씬이 2개 이상 열려 있지만 SceneNameSO 에서 " +
                "식별되는 게임플레이 씬이 없다. 부팅 전환을 건너뛴다.", this);
        }
    }
}
