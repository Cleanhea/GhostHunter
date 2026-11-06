#if UNITY_EDITOR
using System.Collections;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Lift;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 저장된 <c>Lift.prefab</c> 그대로(회전 고정·가구 정의·발판 분리·버튼 배선) 호스트 세션에 스폰해 검증한다.
    /// <see cref="LiftFlowTests"/>가 런타임으로 만든 모형으로 규칙을 보는 반면, 여기서는 프리팹 에셋의 실제 값을 본다 — 에디터 전용.
    /// </summary>
    public sealed class LiftPrefabFlowTests : NetworkFurnitureFixture
    {
        private const string PrefabPath = "Assets/Prefabs/Map/Lift.prefab";

        private GameObject _ground;
        private GameObject _instance;
        private LiftController _lift;
        private GameObject _platformObject;

        [TearDown]
        public void TearDownPrefabLift()
        {
            if (_platformObject != null)
                Object.DestroyImmediate(_platformObject);
            if (_instance != null)
                Object.DestroyImmediate(_instance);
            if (_ground != null)
                Object.DestroyImmediate(_ground);
        }

        private void SpawnPrefabLift(Vector3 position)
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(100f, 1f, 100f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            _instance = Object.Instantiate(prefab, position, Quaternion.identity);
            _instance.GetComponent<NetworkObject>().Spawn();
            _lift = _instance.GetComponent<LiftController>();
            _platformObject = _lift.Platform.gameObject;
        }

        private IEnumerator WaitUntilStopped(float timeoutSeconds = 12f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            yield return null;
            while (_lift.IsMoving && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(_lift.IsMoving, "제한 시간 안에 리프트가 멈추지 않았습니다.");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator 실제_프리팹의_발판은_층_높이만큼_올라가고_다시_내려온다()
        {
            SpawnPrefabLift(new Vector3(0f, 0f, 3f));
            yield return new WaitForSeconds(0.5f); // 본체가 지면에 안착

            float floorHeight = _lift.Settings.FloorHeight;
            Assert.AreEqual(3f, floorHeight, 0.0001f, "프리팹 기본 설정은 프로토타입 3m");
            float restY = _lift.Platform.position.y;
            Assert.IsTrue(_lift.Platform.parent == null, "발판은 실행 때 본체에서 떨어져 나와야 한다");

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            yield return WaitUntilStopped();
            Assert.AreEqual(restY + floorHeight, _lift.Platform.position.y, 0.02f);

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Down));
            yield return WaitUntilStopped();
            Assert.AreEqual(restY, _lift.Platform.position.y, 0.02f);
        }

        [UnityTest]
        public IEnumerator 실제_프리팹의_본체를_밀면_수평으로_미끄러지고_뒤집히지_않으며_발판이_따라온다()
        {
            SpawnPrefabLift(new Vector3(0f, 0f, 3f));
            yield return new WaitForSeconds(0.5f);

            var target = _instance.GetComponent<FurnitureGrabTarget>();
            Rigidbody body = _instance.GetComponent<Rigidbody>();
            Assert.IsFalse(body.isKinematic, "서버에서 본체는 동적이어야 한다");
            Vector3 start = body.position;
            float platformOffset = _lift.Platform.position.z - start.z;

            // 살짝 눌러 바로 떼기 — 위쪽을 조준하고 밀어도 위로 뜨지 않는다.
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            target.ServerRelease(HostClientId, new Vector3(0f, 0.8f, 1f), false);
            yield return new WaitForSeconds(2f);

            float tap = body.position.z - start.z;
            Assert.Greater(tap, 0.02f, "살짝 눌러도 조금은 밀려야 한다");
            Assert.AreEqual(start.y, body.position.y, 0.05f, "공중에 떴다");

            // 최대 차지(1초)로 밀기.
            Vector3 beforeFull = body.position;
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            yield return new WaitForSeconds(1.1f);
            target.ServerRelease(HostClientId, new Vector3(0f, 0.8f, 1f), false);
            yield return new WaitForSeconds(3f);

            float full = body.position.z - beforeFull.z;
            Debug.Log($"[LiftProbe] 밀기 이동 거리 — 살짝 {tap:F2}m, 최대 차지 {full:F2}m");
            Assert.Greater(full, 1.0f, "최대 차지로 밀어도 멀리 가지 않았다");
            Assert.Less(full, 8f, "너무 멀리 미끄러진다");
            Assert.AreEqual(start.y, body.position.y, 0.05f, "공중에 떴다");
            Assert.Less(Quaternion.Angle(Quaternion.identity, body.rotation), 0.5f, "회전했다/뒤집혔다");
            Assert.AreEqual(platformOffset, _lift.Platform.position.z - body.position.z, 0.15f, "발판이 본체를 따라오지 못했다");
        }

        [UnityTest]
        public IEnumerator 실제_프리팹은_두_번째_홀더를_받지_않는다()
        {
            SpawnPrefabLift(new Vector3(0f, 0f, 3f));
            yield return null;

            var target = _instance.GetComponent<FurnitureGrabTarget>();
            Assert.AreEqual(1, target.MaxHoldersAllowed);
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.IsFalse(target.CanGrab(PartnerClientId));
            Assert.IsFalse(target.ServerTryAddHolder(PartnerClientId, AimOrigin, AimDirection));
            Assert.AreEqual(FurnitureState.ThrowReady, target.State);
        }
    }
}
#endif
