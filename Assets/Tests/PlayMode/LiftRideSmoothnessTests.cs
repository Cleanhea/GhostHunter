#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Lift;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 리프트를 타고 오르내릴 때 화면이 떨리지 않는지 — 실제 <c>Lift.prefab</c> 에 <see cref="CharacterController"/> 탑승자
    /// (실제 <see cref="PlatformCarry"/>, <c>PlayerMotor</c> 와 같은 Move 순서)를 올려 프레임마다 재 본다.
    /// 배경: 올라갈 때 탑승자가 0 0 0 0 62mm 0 0 0… 식으로 계단처럼 뛰었다. 발판 콜라이더는 물리 스텝(50Hz)마다만 뛰는데
    /// 탑승자는 보간된 화면 위치로 변위를 재고, 접지 중력이 그 상승분을 덮어 발이 콜라이더를 파고들다 스킨 폭을 넘을 때만
    /// 튀어 올랐기 때문이다. 또 높이를 프레임마다 갱신해 물리 스텝이 읽는 증가량도 들쭉날쭉했다.
    /// </summary>
    public sealed class LiftRideSmoothnessTests : NetworkFurnitureFixture
    {
        private const string PrefabPath = "Assets/Prefabs/Map/Lift.prefab";

        // 화면에 보이는 높이가 발판 화면 위치에서 벗어나도 되는 폭(m).
        private const float VisibleTolerance = 0.004f;

        // 물리상 발이 한 프레임에 뛸 수 있는 최대치 — CharacterController 스킨 폭(0.05) 아래여야 '튀어 오름'이 없다.
        private const float MaxPhysicalStep = 0.045f;

        private GameObject _ground;
        private GameObject _instance;
        private GameObject _rider;
        private GameObject _crate;
        private LiftController _lift;
        private int _previousTargetFrameRate;
        private int _previousVSync;
        private bool _frameRateSaved;

        private sealed class RiderSim : MonoBehaviour
        {
            public CharacterController Controller;
            public Transform Platform;
            public bool Recording;
            public readonly List<float> VisibleMinusPlatform = new();
            public readonly List<float> VisibleDy = new();
            public readonly List<float> PlatformDy = new();
            public readonly List<float> PhysicalDy = new();
            public readonly List<bool> Grounded = new();

            private readonly PlatformCarry _carry = new();
            private float _verticalVelocity;
            private float _lastVisible;
            private float _lastPlatform;
            private float _lastPhysical;
            private bool _hasLast;

            private void Update()
            {
                float dt = Time.deltaTime;
                bool grounded = Controller.isGrounded;
                if (grounded && _verticalVelocity < 0f)
                    _verticalVelocity = -2f;
                _verticalVelocity += -20f * dt; // PlayerMoveSettings 의 중력

                _carry.Move(Controller, new Vector3(0f, _verticalVelocity, 0f) * dt, grounded);

                if (!Recording)
                {
                    _hasLast = false;
                    return;
                }

                float visible = transform.position.y + _carry.VisualOffset.y;
                float platform = Platform.position.y;
                float physical = transform.position.y;
                VisibleMinusPlatform.Add(visible - platform);
                Grounded.Add(grounded);
                if (_hasLast)
                {
                    VisibleDy.Add(visible - _lastVisible);
                    PlatformDy.Add(platform - _lastPlatform);
                    PhysicalDy.Add(physical - _lastPhysical);
                }

                _lastVisible = visible;
                _lastPlatform = platform;
                _lastPhysical = physical;
                _hasLast = true;
            }
        }

        [TearDown]
        public void TearDownRide()
        {
            if (_frameRateSaved)
            {
                Application.targetFrameRate = _previousTargetFrameRate;
                QualitySettings.vSyncCount = _previousVSync;
                _frameRateSaved = false;
            }

            if (_crate != null) Object.DestroyImmediate(_crate);
            if (_rider != null) Object.DestroyImmediate(_rider);
            if (_lift != null && _lift.Platform != null) Object.DestroyImmediate(_lift.Platform.gameObject);
            if (_instance != null) Object.DestroyImmediate(_instance);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 올라갈_때_탑승자가_보는_높이는_발판을_일정하게_따라가고_발이_튀지_않는다()
        {
            yield return Ride(LiftDirection.Up, withCrate: false);
        }

        [UnityTest]
        public IEnumerator 내려갈_때도_탑승자가_보는_높이는_발판을_일정하게_따라간다()
        {
            yield return Ride(LiftDirection.Down, withCrate: false);
        }

        [UnityTest]
        public IEnumerator 발판_위_짐은_오르내리는_동안_발판_대비_높이를_유지한다()
        {
            yield return Ride(LiftDirection.Up, withCrate: true);
            yield return Ride(LiftDirection.Down, withCrate: true);
        }

        private IEnumerator Ride(LiftDirection direction, bool withCrate)
        {
            // 평소 화면 주사율에 가깝게 — 물리 스텝(50Hz)과 맞지 않는 프레임 길이에서 재야 한다.
            if (!_frameRateSaved)
            {
                _previousTargetFrameRate = Application.targetFrameRate;
                _previousVSync = QualitySettings.vSyncCount;
                _frameRateSaved = true;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            if (_instance == null)
                SpawnLift();
            if (direction == LiftDirection.Down && _lift.Platform.position.y < 1f)
            {
                Assert.IsTrue(_lift.ServerPress(LiftDirection.Up));
                yield return WaitStopped();
                yield return new WaitForSeconds(0.3f);
            }

            BoxCollider deck = _lift.Platform.GetComponentInChildren<BoxCollider>();
            float deckTop = deck.bounds.max.y;

            if (_rider != null)
                Object.DestroyImmediate(_rider);
            _rider = new GameObject("Rider");
            _rider.transform.position = new Vector3(
                _lift.Platform.position.x, deckTop + 0.02f, _lift.Platform.position.z);
            var controller = _rider.AddComponent<CharacterController>();
            // Player.prefab 의 값.
            controller.height = 1.3f;
            controller.radius = 0.3f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.05f;
            controller.minMoveDistance = 0.001f;
            controller.center = new Vector3(0f, 0.65f, 0f);
            var sim = _rider.AddComponent<RiderSim>();
            sim.Controller = controller;
            sim.Platform = _lift.Platform;

            if (_crate != null)
                Object.DestroyImmediate(_crate);
            if (withCrate)
            {
                _crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _crate.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                _crate.transform.position = new Vector3(
                    _lift.Platform.position.x + 1.0f, deckTop + 0.25f, _lift.Platform.position.z);
                var body = _crate.AddComponent<Rigidbody>();
                body.mass = 5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
            }

            yield return new WaitForSeconds(0.7f); // 얹혀 안착

            float crateRest = withCrate ? _crate.transform.position.y - _lift.Platform.position.y : 0f;
            var crateOffsets = new List<float>();
            sim.Recording = true;
            Assert.IsTrue(_lift.ServerPress(direction));
            yield return null;
            while (_lift.IsMoving)
            {
                if (withCrate)
                    crateOffsets.Add(_crate.transform.position.y - _lift.Platform.position.y);
                yield return null;
            }
            sim.Recording = false;

            // 가속·감속 구간을 빼고 정속 구간만 본다.
            int n = sim.VisibleDy.Count;
            Assert.Greater(n, 30, "측정할 프레임이 너무 적다");
            int a = (int)(n * 0.3f), b = (int)(n * 0.7f);

            float offsetMin = sim.VisibleMinusPlatform.Skip(a).Take(b - a).Min();
            float offsetMax = sim.VisibleMinusPlatform.Skip(a).Take(b - a).Max();
            Assert.Less(offsetMax - offsetMin, VisibleTolerance,
                $"탑승자 눈높이가 발판 화면 위치에서 {(offsetMax - offsetMin) * 1000f:F1}mm 흔들린다 (min {offsetMin:F4}, max {offsetMax:F4})");

            float worstFrameGap = 0f;
            for (int i = a; i < b; i++)
                worstFrameGap = Mathf.Max(worstFrameGap, Mathf.Abs(sim.VisibleDy[i] - sim.PlatformDy[i]));
            Assert.Less(worstFrameGap, VisibleTolerance,
                $"프레임 변위가 발판과 {worstFrameGap * 1000f:F1}mm 어긋난다 — 달달 떨리는 움직임");

            float worstPhysicalStep = sim.PhysicalDy.Skip(a).Take(b - a).Select(Mathf.Abs).Max();
            Assert.Less(worstPhysicalStep, MaxPhysicalStep,
                $"물리상 발이 한 프레임에 {worstPhysicalStep * 1000f:F1}mm 튀었다 (스킨 폭 50mm)");

            Assert.AreEqual(0, sim.Grounded.Skip(a).Take(b - a).Count(g => !g),
                "발판 위에서 isGrounded 가 풀린 프레임이 있다");

            if (withCrate)
            {
                int ca = (int)(crateOffsets.Count * 0.2f), cb = (int)(crateOffsets.Count * 0.9f);
                List<float> mid = crateOffsets.Skip(ca).Take(cb - ca).ToList();
                Assert.Less(Mathf.Abs(mid.Max() - crateRest), 0.012f, "짐이 발판 위로 떴다/튀었다");
                Assert.Less(Mathf.Abs(crateRest - mid.Min()), 0.012f, "짐이 발판 안으로 파고들었다");
            }
        }

        private void SpawnLift()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(100f, 1f, 100f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            _instance = Object.Instantiate(prefab, new Vector3(0f, 0f, 3f), Quaternion.identity);
            _instance.GetComponent<NetworkObject>().Spawn();
            _lift = _instance.GetComponent<LiftController>();
        }

        private IEnumerator WaitStopped(float timeoutSeconds = 12f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            yield return null;
            while (_lift.IsMoving && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsFalse(_lift.IsMoving, "제한 시간 안에 리프트가 멈추지 않았습니다.");
        }
    }
}
#endif
