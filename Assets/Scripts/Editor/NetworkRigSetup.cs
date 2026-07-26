using GhostHunter.DebugTools;
using GhostHunter.Networking;
using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 네트워크 리그(NetworkManager + 트랜스포트 2종 + Steam 로비 + 접속 HUD)를 한 번에 만든다.
    ///
    /// 손으로 배선하면 컴포넌트 5개와 직렬화 참조 3개를 빠짐없이 연결해야 하는데,
    /// 하나만 빠져도 런타임에 조용히 실패한다(특히 ConnectionManager의 트랜스포트 참조).
    /// 그 실수를 없애려고 도구로 만들었다.
    /// </summary>
    public static class NetworkRigSetup
    {
        private const string RigName = "NetworkRig";

        [MenuItem("GhostHunter/네트워크 리그 생성", priority = 0)]
        public static void CreateNetworkRig()
        {
            var existing = Object.FindFirstObjectByType<NetworkManager>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.LogWarning(
                    $"[NetworkRigSetup] 씬에 이미 NetworkManager 가 있습니다 ('{existing.name}'). " +
                    "중복 생성하지 않고 선택만 했습니다.");
                return;
            }

            var go = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(go, "Create Network Rig");

            // 순서 주의: NetworkManager 를 먼저 붙여야 트랜스포트들이 붙을 때
            // NetworkManager 를 찾아 자기 자신을 등록할 수 있다.
            var networkManager = Undo.AddComponent<NetworkManager>(go);
            var steamTransport = Undo.AddComponent<FacepunchTransport>(go);
            var localTransport = Undo.AddComponent<UnityTransport>(go);
            Undo.AddComponent<SteamLobbyManager>(go);
            var connection = Undo.AddComponent<ConnectionManager>(go);
            Undo.AddComponent<ConnectionHud>(go);

            ConfigureNetworkManager(networkManager, steamTransport);
            WireConnectionManager(connection, networkManager, steamTransport, localTransport);

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log(
                "[NetworkRigSetup] 네트워크 리그를 만들었습니다.\n" +
                "다음 할 일:\n" +
                "  1. NetworkManager 의 Player Prefab 을 지정 (플레이어 프리팹이 생긴 뒤)\n" +
                "  2. 씬 저장\n" +
                "  3. 플레이 → F1 HUD 에서 Host / Join 테스트");
        }

        private static void ConfigureNetworkManager(NetworkManager networkManager, NetworkTransport defaultTransport)
        {
            // NetworkConfig 는 초기화자 없는 public 필드라 상황에 따라 null 일 수 있다.
            networkManager.NetworkConfig ??= new NetworkConfig();

            // 기본은 Steam 경로. 로컬 테스트는 ConnectionManager 가 런타임에 갈아끼운다.
            networkManager.NetworkConfig.NetworkTransport = defaultTransport;
            networkManager.NetworkConfig.EnableSceneManagement = true;

            // 트랜스포트가 Steam 연결 과정을 Developer 레벨로 로그한다.
            // 초기 세팅 단계에서는 이 로그가 없으면 어디서 막혔는지 알 수 없다.
            networkManager.LogLevel = LogLevel.Developer;

            EditorUtility.SetDirty(networkManager);
        }

        private static void WireConnectionManager(
            ConnectionManager connection,
            NetworkManager networkManager,
            FacepunchTransport steamTransport,
            UnityTransport localTransport)
        {
            // private [SerializeField] 라 코드로 직접 못 넣는다. SerializedObject 로 우회한다.
            // 필드명을 문자열로 쓰는 지점이라, 이름을 바꾸면 여기도 같이 고쳐야 한다.
            var so = new SerializedObject(connection);

            AssignReference(so, "_networkManager", networkManager);
            AssignReference(so, "_steamTransport", steamTransport);
            AssignReference(so, "_localTransport", localTransport);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(connection);
        }

        private static void AssignReference(SerializedObject so, string propertyPath, Object value)
        {
            SerializedProperty property = so.FindProperty(propertyPath);

            if (property == null)
            {
                // 필드명이 바뀌었는데 이 도구를 안 고친 경우. 조용히 넘어가면
                // "리그를 만들었는데 동작은 안 하는" 상태가 되므로 크게 알린다.
                Debug.LogError(
                    $"[NetworkRigSetup] ConnectionManager 에서 '{propertyPath}' 필드를 찾지 못했습니다. " +
                    "필드명이 바뀌었다면 NetworkRigSetup.cs 도 함께 고쳐야 합니다.");
                return;
            }

            property.objectReferenceValue = value;
        }
    }
}
