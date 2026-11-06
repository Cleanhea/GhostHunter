using System.Collections;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Lift;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 리프트 승강 발판 흐름(lift-system.md §2.3)과 본체 취급(2인 잡기 금지·수평 발사)을 실제 호스트 세션과 PhysX 로 검증한다.
    /// 리프트는 프리팹 에셋이 아니라 테스트에서 만든 런타임 오브젝트다 — 검증 대상은 배치가 아니라 서버 상태 기계다.
    /// </summary>
    public sealed class LiftFlowTests : NetworkFurnitureFixture
    {
        private const float Floor = 3f;
        private const float DeckRestY = 0.24f;

        private LiftSettings _liftSettings;
        private GameObject _liftObject;
        private LiftController _lift;
        private Transform _platform;
        private LiftButton _upButton;
        private LiftButton _downButton;
        private readonly System.Collections.Generic.List<GameObject> _extras = new();

        [TearDown]
        public void TearDownLift()
        {
            foreach (GameObject extra in _extras)
            {
                if (extra != null)
                    Object.DestroyImmediate(extra);
            }

            _extras.Clear();
            if (_liftObject != null)
                Object.DestroyImmediate(_liftObject);
            if (_liftSettings != null)
                Object.DestroyImmediate(_liftSettings);
        }

        private LiftController SpawnLift(Vector3 buttonPosition = default)
        {
            _liftSettings = ScriptableObject.CreateInstance<LiftSettings>();
            SetPrivateField(_liftSettings, "_floorHeight", Floor);
            SetPrivateField(_liftSettings, "_stopCount", 2);
            SetPrivateField(_liftSettings, "_maxSpeed", 6f);
            SetPrivateField(_liftSettings, "_acceleration", 24f);
            SetPrivateField(_liftSettings, "_clientSmoothTime", 0f);

            _liftObject = new GameObject("TestLift");
            _liftObject.SetActive(false);
            _liftObject.transform.position = new Vector3(0f, 0f, 30f);

            Rigidbody rootBody = _liftObject.AddComponent<Rigidbody>();
            rootBody.isKinematic = true;
            _liftObject.AddComponent<NetworkObject>();

            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis";
            chassis.transform.SetParent(_liftObject.transform, false);
            chassis.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            chassis.transform.localScale = new Vector3(3.8f, 0.2f, 2.2f);

            var platformObject = new GameObject("Platform");
            platformObject.transform.SetParent(_liftObject.transform, false);
            platformObject.transform.localPosition = new Vector3(0f, DeckRestY, 0f);
            Rigidbody platformBody = platformObject.AddComponent<Rigidbody>();
            platformBody.isKinematic = true;
            BoxCollider deck = platformObject.AddComponent<BoxCollider>();
            deck.size = new Vector3(3f, 0.08f, 2f);
            platformObject.AddComponent<LiftPlatform>();
            _platform = platformObject.transform;

            _upButton = CreateButton("Button_Up", LiftDirection.Up, buttonPosition);
            _downButton = CreateButton("Button_Down", LiftDirection.Down, buttonPosition);

            var lift = _liftObject.AddComponent<LiftController>();
            SetPrivateField(lift, "_settings", _liftSettings);
            SetPrivateField(lift, "_platform", _platform);
            SetPrivateField(lift, "_deck", deck);
            SetPrivateField(lift, "_buttons", new[] { _upButton, _downButton });

            AssignPrefabHash(_liftObject.GetComponent<NetworkObject>());
            _liftObject.SetActive(true);
            _liftObject.GetComponent<NetworkObject>().Spawn();
            _lift = lift;
            return lift;
        }

        private LiftButton CreateButton(string name, LiftDirection direction, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_liftObject.transform, false);
            go.transform.localPosition = localPosition;
            var button = go.AddComponent<LiftButton>();
            SetPrivateField(button, "_direction", direction);
            return button;
        }

        private IEnumerator WaitUntilStopped(float timeoutSeconds = 8f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            yield return null;
            while (_lift.IsMoving && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsFalse(_lift.IsMoving, "제한 시간 안에 리프트가 멈추지 않았습니다.");

            // 발판 몸체는 물리 스텝마다 논리 높이를 따라온다.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private GameObject Extra(GameObject go)
        {
            _extras.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator 상승_버튼은_한_층_위_정지_포인트까지_올라가_멈춘다()
        {
            SpawnLift();
            yield return null;

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            Assert.IsTrue(_lift.IsMoving);
            yield return WaitUntilStopped();

            Assert.AreEqual(Floor, _lift.Height, 0.001f);
            Assert.AreEqual(DeckRestY + Floor, _platform.localPosition.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator 하강_버튼은_가장_가까운_아래_정지_포인트로_내려간다()
        {
            SpawnLift();
            yield return null;
            _lift.ServerPress(LiftDirection.Up);
            yield return WaitUntilStopped();

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Down));
            yield return WaitUntilStopped();

            Assert.AreEqual(0f, _lift.Height, 0.001f);
            Assert.AreEqual(DeckRestY, _platform.localPosition.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator 이동_중에는_어느_버튼도_무시한다()
        {
            SpawnLift();
            yield return null;
            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            yield return null;
            yield return null;

            Assert.IsTrue(_lift.IsMoving);
            Assert.IsFalse(_lift.ServerPress(LiftDirection.Up), "이동 중 같은 방향 입력은 무시");
            Assert.IsFalse(_lift.ServerPress(LiftDirection.Down), "이동 중 반대 방향 입력도 무시");

            yield return WaitUntilStopped();
            Assert.AreEqual(Floor, _lift.Height, 0.001f, "무시된 입력이 목표를 바꾸면 안 된다");
        }

        [UnityTest]
        public IEnumerator 끝에서_더_갈_수_없는_방향은_움직이지_않는다()
        {
            SpawnLift();
            yield return null;

            Assert.IsFalse(_lift.ServerPress(LiftDirection.Down), "맨 아래에서 하강");
            Assert.IsFalse(_lift.IsMoving);
            Assert.IsTrue(_lift.CanMove(LiftDirection.Up));
            Assert.IsFalse(_lift.CanMove(LiftDirection.Down));
        }

        [UnityTest]
        public IEnumerator 경로가_막히면_그_자리에서_멈추고_다음_입력은_가장_가까운_정지_포인트로_간다()
        {
            SpawnLift();
            // 발판 위 1.5m 높이를 가로막는 천장 판.
            GameObject ceiling = Extra(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ceiling.transform.position = new Vector3(0f, 1.6f, 30f);
            ceiling.transform.localScale = new Vector3(4f, 0.2f, 3f);
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            yield return WaitUntilStopped();

            // 발판 윗면(0.28 + 높이)이 천장 아랫면 1.5m 앞에서 멈춘다.
            Assert.Less(_lift.Height, 1.25f, "막힌 천장을 뚫고 올라갔다");
            Assert.Greater(_lift.Height, 0.9f, "막힌 지점이 아니라 너무 일찍 멈췄다");
            float stoppedAt = _lift.Height;

            Object.DestroyImmediate(ceiling);
            yield return new WaitForFixedUpdate();

            // 장애물이 사라진 뒤 상승: 제자리에서 한 층을 더 오르는 게 아니라 가장 가까운 위 정지 포인트(3m)에서 멈춘다.
            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            yield return WaitUntilStopped();
            Assert.AreEqual(Floor, _lift.Height, 0.001f);
            Assert.Greater(Floor, stoppedAt);
        }

        [UnityTest]
        public IEnumerator 막혀_중간에_선_뒤_하강은_맨_아래_정지_포인트로_간다()
        {
            SpawnLift();
            GameObject ceiling = Extra(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ceiling.transform.position = new Vector3(0f, 1.6f, 30f);
            ceiling.transform.localScale = new Vector3(4f, 0.2f, 3f);
            yield return new WaitForFixedUpdate();

            _lift.ServerPress(LiftDirection.Up);
            yield return WaitUntilStopped();
            Assert.Greater(_lift.Height, 0.5f);
            Assert.Less(_lift.Height, Floor);

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Down));
            yield return WaitUntilStopped();
            Assert.AreEqual(0f, _lift.Height, 0.001f);
        }

        [UnityTest]
        public IEnumerator 발판에_얹힌_짐은_막힘이_아니라_함께_올라간다()
        {
            SpawnLift();
            GameObject cargo = Extra(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cargo.transform.position = new Vector3(0f, DeckRestY + 0.04f + 0.3f, 30f);
            cargo.transform.localScale = Vector3.one * 0.5f;
            Rigidbody cargoBody = cargo.AddComponent<Rigidbody>();
            cargoBody.mass = 8f;
            // 발판 위에 얹혀 안정될 때까지 둔다.
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
            yield return WaitUntilStopped();

            Assert.AreEqual(Floor, _lift.Height, 0.001f, "짐 때문에 막혀 중간에 멈췄다");
            for (int i = 0; i < 10; i++)
                yield return new WaitForFixedUpdate();
            Assert.Greater(cargo.transform.position.y, Floor + DeckRestY,
                "짐이 발판과 함께 올라오지 못했다");
        }

        [UnityTest]
        public IEnumerator 버튼에서_먼_플레이어의_요청은_서버가_거절한다()
        {
            // 호스트 플레이어는 원점, 버튼은 30m 밖.
            SpawnLift(buttonPosition: Vector3.zero);
            yield return null;

            _lift.RequestPress(LiftDirection.Up);
            yield return new WaitForSeconds(0.3f);

            Assert.IsFalse(_lift.IsMoving);
            Assert.AreEqual(0f, _lift.Height, 0.001f);
        }

        [UnityTest]
        public IEnumerator 버튼_가까이에서_보낸_요청은_서버가_받아_움직인다()
        {
            SpawnLift();
            // 버튼을 호스트 플레이어(원점) 눈높이 근처로 옮긴다.
            _upButton.transform.position = new Vector3(0f, 1.2f, 1f);
            _downButton.transform.position = new Vector3(0f, 1.2f, 1f);
            yield return null;

            _lift.RequestPress(LiftDirection.Up);
            yield return null;
            yield return null;
            Assert.IsTrue(_lift.IsMoving, "가까운 플레이어의 요청이 서버에 받아들여지지 않았다");

            yield return WaitUntilStopped();
            Assert.AreEqual(Floor, _lift.Height, 0.001f);
        }

        [UnityTest]
        public IEnumerator 정의가_홀더를_1명으로_제한하면_두_번째_홀더는_거절된다()
        {
            SetPrivateField(Definition, "_maxHolders", 1);
            FurnitureGrabTarget target = SpawnFurniture();
            yield return null;

            Assert.AreEqual(1, target.MaxHoldersAllowed);
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            Assert.IsFalse(target.HasFreeSlot);
            Assert.IsFalse(target.CanGrab(PartnerClientId), "두 번째 홀더는 조준 불가여야 한다");
            Assert.IsFalse(target.ServerTryAddHolder(PartnerClientId, AimOrigin, AimDirection));
            Assert.AreEqual(FurnitureState.ThrowReady, target.State, "Held 로 올라가 부양하면 안 된다");
        }

        [UnityTest]
        public IEnumerator 수평_발사만_켜면_위로_뜨지_않고_회전도_주지_않는다()
        {
            SetPrivateField(Definition, "_horizontalLaunchOnly", true);
            SetPrivateField(Definition, "_launchSpeedScale", 0.25f);
            FurnitureGrabTarget target = SpawnFurniture();
            yield return null;

            Rigidbody body = target.GetComponent<Rigidbody>();
            Assert.IsTrue(target.ServerTryAddHolder(HostClientId, AimOrigin, AimDirection));
            target.ServerRelease(HostClientId, new Vector3(0f, 0.9f, 1f), false);

            // 힘은 다음 물리 스텝에 속도로 반영된다. 그 스텝의 중력(−0.2m/s)만 아래로 섞인다.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 velocity = body.linearVelocity;
            Assert.LessOrEqual(velocity.y, 0.01f, "위쪽 조준이어도 위로 뜨면 안 된다");
            Assert.AreEqual(Vector3.zero, body.angularVelocity, "수평으로만 미는 본체는 회전하지 않는다");

            float expected = Settings.OneHolderForce * Settings.ForceRatio(0f) * 0.25f;
            Assert.AreEqual(expected, velocity.z, 0.05f, "발사 속도 배율이 적용되지 않았다");
            Assert.AreEqual(0f, velocity.x, 0.01f);
        }
    }
}
