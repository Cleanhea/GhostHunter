using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace GhostHunter.Systems.SceneFlow
{
    /// <summary>로컬·NGO 씬 작업과 완료/세션 종료 대기. 전환 권한·순서는 Controller가 맡는다.</summary>
    internal sealed class SceneTransitionOperations
    {
        private enum Outcome { Rejected, Completed, SessionEnded }

        private readonly Object _context;
        private readonly CancellationToken _cancellationToken;

        public SceneTransitionOperations(Object context, CancellationToken cancellationToken)
        {
            _context = context;
            _cancellationToken = cancellationToken;
        }

        public static bool UsesNgo(NetworkManager network) => network != null && network.IsListening
            && !network.ShutdownInProgress && network.NetworkConfig.EnableSceneManagement;

        public async UniTask<Scene> LoadThroughNgoAsync(NetworkManager network, string sceneName)
        {
            if (await RunNgoOperationAsync(network, sceneName, default, loading: true) == Outcome.Rejected)
                return default;
            // 세션이 끝나도 이미 올라온 씬은 받아들여 서비스가 등록된 채 남지 않게 한다.
            Scene loaded = SceneManager.GetSceneByName(sceneName);
            return loaded.isLoaded ? loaded : default;
        }

        public async UniTask UnloadAsync(NetworkManager network, Scene scene)
        {
            if (!UsesNgo(network) || !network.IsServer)
            {
                await UnloadLocallyAsync(scene);
                return;
            }
            Outcome outcome = await RunNgoOperationAsync(network, scene.name, scene, loading: false);
            if (outcome == Outcome.SessionEnded && scene.IsValid() && scene.isLoaded)
                await UnloadLocallyAsync(scene);
        }

        public async UniTask<Scene> LoadLocallyAsync(string sceneName)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation == null)
            {
                Debug.LogError(
                    $"{nameof(SceneFlowController)}: 씬 {sceneName} 로드를 시작하지 못했다. " +
                    "빌드 씬 목록을 확인할 것.", _context);
                return default;
            }

            await operation.ToUniTask(cancellationToken: _cancellationToken);
            return SceneManager.GetSceneByName(sceneName);
        }

        public async UniTaskVoid UnloadAdoptedPreviousSceneAsync(Scene scene)
        {
            string sceneName = scene.name;
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);

            if (operation == null)
            {
                Debug.LogWarning(
                    $"{nameof(SceneFlowController)}: 이전 씬 {sceneName} 을 내리지 못했다.", _context);
                return;
            }

            try
            {
                await operation.ToUniTask(cancellationToken: _cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            Debug.Log(
                $"{nameof(SceneFlowController)}: NGO 씬을 받아들이며 이전 씬 {sceneName} 을 내렸다.", _context);
        }

        /// <summary>
        /// 전환 중에는 두 씬이 겹친다. 이전 씬의 EventSystem 과 AudioListener 를 먼저 끈다.
        /// 둘이 동시에 살아 있으면 입력이 갈리고 콘솔에 AudioListener 중복 경고가 뜬다.
        /// </summary>
        public static List<Behaviour> SuspendInput(Scene scene)
        {
            var suspended = new List<Behaviour>();

            if (!scene.IsValid() || !scene.isLoaded)
                return suspended;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SuspendEnabled(root.GetComponentsInChildren<EventSystem>(true), suspended);
                SuspendEnabled(root.GetComponentsInChildren<AudioListener>(true), suspended);
            }

            return suspended;
        }

        /// <summary>전환이 실패했을 때 이전 씬을 원상 복구한다.</summary>
        public static void RestoreInput(List<Behaviour> suspended)
        {
            foreach (Behaviour behaviour in suspended)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
        }
        private static void SuspendEnabled<T>(T[] behaviours, List<Behaviour> suspended) where T : Behaviour
        {
            foreach (T behaviour in behaviours)
            {
                if (!behaviour.enabled)
                    continue;
                behaviour.enabled = false;
                suspended.Add(behaviour);
            }
        }

        private async UniTask UnloadLocallyAsync(Scene scene)
        {
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            if (operation != null)
                await operation.ToUniTask(cancellationToken: _cancellationToken);
        }

        private async UniTask<Outcome> RunNgoOperationAsync(
            NetworkManager network, string sceneName, Scene scene, bool loading)
        {
            var completion = new UniTaskCompletionSource();
            void OnCompleted(string name, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
            {
                if (name != sceneName)
                    return;
                if (loading && timedOut.Count > 0)
                    Debug.LogWarning($"{nameof(SceneFlowController)}: {sceneName} 로드 완료를 보고하지 않은 클라이언트 — " +
                        string.Join(", ", timedOut), _context);
                completion.TrySetResult();
            }

            if (loading)
                network.SceneManager.OnLoadEventCompleted += OnCompleted;
            else
                network.SceneManager.OnUnloadEventCompleted += OnCompleted;
            try
            {
                SceneEventProgressStatus status = loading
                    ? network.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive)
                    : network.SceneManager.UnloadScene(scene);
                if (status != SceneEventProgressStatus.Started)
                {
                    if (loading)
                        Debug.LogError($"{nameof(SceneFlowController)}: NGO 씬 로드를 시작하지 못했다 — {status}", _context);
                    else
                        Debug.LogWarning($"{nameof(SceneFlowController)}: NGO 씬 언로드를 시작하지 못했다 — {status}", _context);
                    return Outcome.Rejected;
                }
                if (await WaitForSceneEventAsync(network, completion.Task))
                    return Outcome.Completed;
                if (loading)
                    Debug.LogWarning($"{nameof(SceneFlowController)}: {sceneName} 로드를 기다리는 중 세션이 끝났다.", _context);
                return Outcome.SessionEnded;
            }
            finally
            {
                if (network.SceneManager != null)
                {
                    if (loading)
                        network.SceneManager.OnLoadEventCompleted -= OnCompleted;
                    else
                        network.SceneManager.OnUnloadEventCompleted -= OnCompleted;
                }
            }
        }

        /// <summary>
        /// NGO 씬 이벤트 완료를 기다린다. 기다리는 동안 세션이 내려가면 NGO 는 이벤트를 버려 완료가 오지 않는다 —
        /// 그때도 풀려나야 SceneFlowController.IsLoading 에 박혀 이후 전환(타이틀 복귀 포함)이 전부 거부되지 않는다.
        /// 정지 콜백 안에서 이어 달리면 셧다운 도중 NGO 를 다시 건드리므로 프레임마다 확인한다.
        /// </summary>
        /// <returns>이벤트가 완료됐으면 true, 세션이 먼저 끝났으면 false.</returns>
        private async UniTask<bool> WaitForSceneEventAsync(NetworkManager networkManager, UniTask completed)
        {
            int winner = await UniTask.WhenAny(
                completed,
                UniTask.WaitUntil(() => !networkManager.IsListening, cancellationToken: _cancellationToken));
            return winner == 0;
        }
    }
}
