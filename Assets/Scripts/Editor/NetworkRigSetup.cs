using GhostHunter.Networking;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 네트워크 리그 프리팹으로 가는 진입점.
    ///
    /// 예전에는 이 도구가 리그를 씬에 직접 만들었지만, 지금 리그는
    /// <c>Assets/Prefabs/NetworkRig.prefab</c> 하나뿐이고 각 씬은
    /// <see cref="NetworkRigBootstrap"/>으로 "없을 때만" 생성한다. 씬에 리그를 또 만들면
    /// 부트스트랩과 경쟁해 <see cref="NetworkManager"/>가 둘 생길 수 있어(NGO는 중복을
    /// 정리해 주지 않는다) 생성 대신 프리팹을 열어 주기만 한다.
    /// </summary>
    public static class NetworkRigSetup
    {
        [MenuItem("GhostHunter/네트워크 리그 프리팹 열기", priority = 0)]
        public static void SelectNetworkRigPrefab()
        {
            GameObject prefab = PrototypeSceneSetup.EnsureNetworkRigPrefab();

            Selection.activeGameObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            var sceneRig = Object.FindFirstObjectByType<NetworkManager>();
            if (sceneRig != null)
            {
                Debug.LogWarning(
                    $"[NetworkRigSetup] 현재 씬에 NetworkManager 가 직접 배치되어 있습니다 ('{sceneRig.name}').\n" +
                    "리그는 프리팹 + NetworkBootstrap 으로만 들어가야 합니다. 씬의 리그를 지우고 " +
                    "'GhostHunter > 프로토타입 게임 생성' 으로 씬을 재생성하세요.");
            }

            Debug.Log(
                "[NetworkRigSetup] 리그 프리팹을 선택했습니다.\n" +
                "리그 구성을 바꾸려면 이 프리팹을 편집하거나 PrototypeSceneSetup." +
                nameof(PrototypeSceneSetup.CreateOrUpdateNetworkRigPrefab) + " 를 고친다.\n" +
                "씬에 리그를 추가하는 방법은 NetworkBootstrap 오브젝트뿐이다.");
        }
    }
}
