using System.Collections;
using System.Reflection;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 호스트 하나짜리 Netcode 세션과 그 위에 스폰된 가구를 만들어 주는 테스트 토대.
    ///
    /// 트랜스포트는 <see cref="UnityTransport"/>를 쓴다. Steam(Facepunch)은 batchmode 에
    /// Steam 클라이언트가 없어 초기화부터 실패하고, 계정당 인스턴스가 하나라 2인을 만들 수도
    /// 없다 → workflow/testing.md §4.1
    ///
    /// 가구를 프리팹 <b>에셋</b>에서 불러오지 않는 이유: 테스트가 AssetDatabase 에 매이면
    /// 플레이어 빌드에서 돌릴 수 없고, 맵 생성 도구가 만든 가구 치수에 결과가 끌려다닌다.
    /// 여기서 검증하려는 것은 배치가 아니라 <see cref="FurnitureGrabTarget"/> 상태 기계다.
    /// </summary>
    public abstract class NetworkFurnitureFixture
    {
        /// <summary>접속하지 않은 두 번째 홀더. 2인 잡기 규칙만 검사할 때 쓴다.</summary>
        protected const ulong PartnerClientId = 999;

        private const float StartTimeoutSeconds = 15f;

        private GameObject _networkManagerObject;
        private GameObject _furnitureTemplate;
        private GameObject _playerTemplate;
        private GameObject _playerInstance;
        private FurnitureThrowSettings _settings;
        private FurnitureDefinition _definition;
        private uint _nextPrefabHash = 0x5F000001;

        protected NetworkManager Network { get; private set; }

        protected FurnitureThrowSettings Settings => _settings;

        protected ulong HostClientId => Network.LocalClientId;

        /// <summary>호스트 플레이어의 눈높이. 홀더 조준의 시작점으로 쓴다.</summary>
        protected Vector3 AimOrigin => _playerInstance.transform.position + Vector3.up * 1.65f;

        protected Vector3 AimDirection => Vector3.forward;

        [UnitySetUp]
        public IEnumerator SetUpSession()
        {
            _settings = ScriptableObject.CreateInstance<FurnitureThrowSettings>();
            _definition = ScriptableObject.CreateInstance<FurnitureDefinition>();

            _playerTemplate = CreatePlayerTemplate();
            _furnitureTemplate = CreateFurnitureTemplate();

            _networkManagerObject = new GameObject("TestNetworkManager");
            _networkManagerObject.SetActive(false);

            var network = _networkManagerObject.AddComponent<NetworkManager>();
            var transport = _networkManagerObject.AddComponent<UnityTransport>();
            network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,

                // 씬 동기화는 접속한 클라이언트가 있어야 의미가 있고, 테스트 씬을 NGO 가
                // 관리하려 들면 러너가 만든 씬과 충돌한다.
                EnableSceneManagement = false,

                // 플레이어는 아래에서 직접 스폰한다. 프리팹 에셋이 아니라 런타임 오브젝트라
                // NGO 의 자동 스폰 경로(프리팹 목록 조회)에 태우지 않는다.
                PlayerPrefab = null,
            };

            _networkManagerObject.SetActive(true);
            Network = network;

            Assert.IsTrue(network.StartHost(), "Local Host 시작에 실패했습니다.");

            float deadline = Time.realtimeSinceStartup + StartTimeoutSeconds;
            while (!network.IsHost && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(network.IsHost, "제한 시간 안에 호스트가 되지 않았습니다.");

            _playerInstance = Object.Instantiate(_playerTemplate);
            _playerInstance.SetActive(true);
            _playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(network.LocalClientId);

            yield return null;

            Assert.IsNotNull(
                network.LocalClient.PlayerObject,
                "호스트 플레이어 오브젝트가 스폰되지 않았습니다.");
        }

        [UnityTearDown]
        public IEnumerator TearDownSession()
        {
            if (Network != null)
            {
                Network.Shutdown();
                yield return null;
            }

            DestroyIfPresent(ref _networkManagerObject);
            DestroyIfPresent(ref _playerInstance);
            DestroyIfPresent(ref _playerTemplate);
            DestroyIfPresent(ref _furnitureTemplate);

            if (_settings != null)
                Object.DestroyImmediate(_settings);
            if (_definition != null)
                Object.DestroyImmediate(_definition);

            Network = null;
        }

        /// <summary>세션에 가구 하나를 스폰한다. 자리는 플레이어 앞 3m.</summary>
        protected FurnitureGrabTarget SpawnFurniture()
        {
            GameObject instance = Object.Instantiate(_furnitureTemplate);
            instance.name = "TestFurniture";
            instance.transform.position = new Vector3(0f, 1f, 3f);

            // 인스턴스마다 해시가 달라야 NGO 가 서로를 구분한다 → conventions/unity-assets.md §5.2
            AssignPrefabHash(instance.GetComponent<NetworkObject>());
            instance.SetActive(true);
            instance.GetComponent<NetworkObject>().Spawn();

            return instance.GetComponent<FurnitureGrabTarget>();
        }

        private GameObject CreatePlayerTemplate()
        {
            var template = new GameObject("TestPlayerTemplate");
            template.SetActive(false);
            AssignPrefabHash(template.AddComponent<NetworkObject>());
            return template;
        }

        private GameObject CreateFurnitureTemplate()
        {
            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "TestFurnitureTemplate";
            template.SetActive(false);

            Rigidbody body = template.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            template.AddComponent<NetworkObject>();
            template.AddComponent<NetworkTransform>();

            var physics = template.AddComponent<FurnitureNetworkPhysics>();
            var target = template.AddComponent<FurnitureGrabTarget>();
            var hover = template.AddComponent<FurnitureHoverMotor>();
            var launcher = template.AddComponent<FurnitureLauncher>();

            SetPrivateField(physics, "_definition", _definition);
            SetPrivateField(target, "_settings", _settings);
            SetPrivateField(hover, "_settings", _settings);
            SetPrivateField(launcher, "_settings", _settings);

            return template;
        }

        /// <summary>
        /// <c>GlobalObjectIdHash</c> 는 에디터가 에셋 경로로 굽는 internal 필드다. 런타임에
        /// 만든 오브젝트에는 0 이 들어 있어 NGO 가 스폰 대상을 구분하지 못한다.
        /// </summary>
        private void AssignPrefabHash(NetworkObject networkObject)
        {
            FieldInfo field = typeof(NetworkObject).GetField(
                "GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(
                field,
                "NetworkObject.GlobalObjectIdHash 가 없습니다. NGO 버전이 바뀌었습니다.");

            field.SetValue(networkObject, _nextPrefabHash);
            _nextPrefabHash++;
        }

        protected static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName} 를 찾지 못했습니다.");
            field.SetValue(target, value);
        }

        private static void DestroyIfPresent(ref GameObject target)
        {
            if (target != null)
                Object.DestroyImmediate(target);

            target = null;
        }
    }
}
