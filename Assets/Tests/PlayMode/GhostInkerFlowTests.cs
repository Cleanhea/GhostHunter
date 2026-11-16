using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// '명령(인커)' 귀신(project/ghost-inker.md)의 서버 흐름을 실제 컨트롤러로 확인한다 — 벽 글씨 쓰기·발견·뒤돌기 위반·닦기,
    /// 문틈 응시의 눈 맞춤·글씨 발견·문 잡기, 어택 시작 시 정리, 확률 발동과 횟수 제한. 서버 인커 틱(<c>ServerTickInker</c>)을
    /// 직접 불러 시간을 진행한다. 인형은 큐브 몸으로 보임·숨김(쓰는 동안 숨김)과 갑툭튀 발광·거리만 본다 — 실제 모습은
    /// <see cref="InkerPresentationRenderTests"/>.
    /// </summary>
    public sealed class GhostInkerFlowTests
    {
        private const float Dt = 0.02f;

        private readonly List<GameObject> _objects = new();
        private GhostInkerSettings _settings;
        private SanitySystemSettings _sanitySettings;
        private SanityTeamService _team;
        private LocalPlayerContext _localPlayer;
        private NetworkManager _network;
        private GhostPrototypeController _ghost;
        private uint _hash = 0x6F720000;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _settings = ScriptableObject.CreateInstance<GhostInkerSettings>();
            _sanitySettings = ScriptableObject.CreateInstance<SanitySystemSettings>();
            _team = Create("InkerTestTeam").AddComponent<SanityTeamService>();
            Services.Bind<ISanityTeamService>(_team);
            _localPlayer = new LocalPlayerContext();
            Services.Bind<ILocalPlayerContext>(_localPlayer);

            GameObject root = Create("InkerTestNetwork");
            root.SetActive(false);
            _network = root.AddComponent<NetworkManager>();
            var transport = root.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 17797, "127.0.0.1");
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
            };
            root.SetActive(true);
            Assert.IsTrue(_network.StartHost());
            yield return null;

            GameObject floor = Create("InkerTestFloor");
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(40f, 0.2f, 40f);

            GameObject ghost = Create("InkerTestGhost");
            ghost.SetActive(false);
            ghost.layer = GameLayers.GhostPrototype;
            ghost.transform.position = new Vector3(-10f, 0.01f, -10f);
            var networkObject = ghost.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            _ghost = ghost.AddComponent<GhostPrototypeController>();
            Set(_ghost, "_settings", _settings);
            ghost.SetActive(true);
            networkObject.Spawn();
            // Update 가 공통 서버 틱(배회·NavMesh)을 돌리지 않게 막고 인커 틱만 직접 부른다.
            Set(_ghost, "_serverReady", false);
            Set(_ghost, "_roamCenter", Vector3.zero);
            Set(_ghost, "_roamExtents", new Vector3(18f, 5f, 18f));
            Assert.IsTrue(_ghost.IsServer);
            Assert.IsTrue(_ghost.IsInker);
            Assert.IsTrue(Services.TryGet(out ICommandWritingService service) && ReferenceEquals(service, _ghost),
                "인커 귀신이 대걸레 닦기 서비스로 등록된다");
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_network != null)
            {
                _network.Shutdown();
                yield return null;
            }

            bool leaked = Services.TryGet(out ICommandWritingService leakedService);
            if (leaked)
                Services.Unbind(leakedService);
            Services.Unbind<ISanityTeamService>(_team);
            Services.Unbind<ILocalPlayerContext>(_localPlayer);
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                    Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_sanitySettings);
            Assert.IsFalse(leaked, "귀신이 사라지면 닦기 서비스도 빠진다");
        }

        [Test]
        public void 벽_글씨를_다_쓴_뒤_본_플레이어가_뒤돌면_정신력_10이_깎이고_글씨가_사라진다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            int before = player.Sanity;

            StartPhenomenon(InkerPhenomenon.WallWriting);
            Assert.AreEqual(1, _ghost.InkerWritingCount);
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out InkerWritingState writing));
            Assert.AreEqual(InkerPhenomenon.WallWriting, writing.Source);
            Assert.AreNotEqual(InkerCommand.None, writing.Command);
            Assert.IsFalse(writing.Complete, "쓰는 중");
            Assert.AreEqual(InkerStep.WallWriting, _ghost.InkerSequence.Step);

            // 쓰는 중에는 위반 판정을 하지 않는다.
            TurnPlayer(player, 0f, 180f, 0.3f);
            TurnPlayer(player, 180f, 360f, 0.3f);
            Assert.AreEqual(before, player.Sanity);

            Tick(4.1f);
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out writing));
            Assert.IsTrue(writing.Complete, "4초 뒤 다 썼다");
            Assert.IsFalse(_ghost.InkerSequence.IsRunning, "다 쓰면 현상이 끝난다(목 돌리기·응시 없음, 2026-11-16)");
            Assert.AreEqual(before, player.Sanity, "현상 2 자체로는 정신력이 줄지 않는다");

            ForceCommand(InkerCommand.DontTurnAround);
            Tick(0.2f);
            TurnPlayer(player, 0f, 180f, 0.4f);

            Assert.AreEqual(before - 10, player.Sanity, "닦지 않고 어기면 −10");
            Assert.AreEqual(0, _ghost.InkerWritingCount, "위반하면 글씨는 자동으로 사라진다");
            Assert.AreEqual(1, _ghost.InkerViolationCount);
        }

        [Test]
        public void 글씨를_보지_못한_플레이어는_명령을_지키지_않아도_된다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            ForceCommand(InkerCommand.DontTurnAround);

            // 다 쓰는 동안 벽을 등지고 있다가, 다 쓴 뒤에 돌아본다 — 돌아보는 동작은 발견 전이라 위반이 아니다.
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Tick(4.1f);
            int before = player.Sanity;
            TurnPlayer(player, 180f, 360f, 0.4f);
            Tick(0.2f);
            Assert.AreEqual(before, player.Sanity, "보기 전에 돈 것은 세지 않는다");
            Assert.AreEqual(1, _ghost.InkerWritingCount);

            // 본 뒤에 다시 뒤돌면 위반.
            TurnPlayer(player, 0f, 180f, 0.4f);
            Assert.AreEqual(before - 10, player.Sanity);
            Assert.AreEqual(0, _ghost.InkerWritingCount);
        }

        [Test]
        public void 글씨를_대걸레로_전부_닦으면_명령이_풀린다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            ForceCommand(InkerCommand.DontTurnAround);
            Tick(4.1f);
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out InkerWritingState writing));
            int id = writing.Id;
            int before = player.Sanity;

            Vector3 eye = player.transform.position + Vector3.up * 1.6f;
            if (!_ghost.TryGetInkerWriting(0, out writing))
                Assert.Fail("글씨가 없다");
            Vector3 aim = (writing.LocalPosition - eye).normalized;
            Assert.IsTrue(_ghost.TryRaycast(eye, aim, 6f, player.transform, out int aimed));
            Assert.AreEqual(id, aimed);
            Assert.IsFalse(_ghost.TryRaycast(eye, Vector3.back, 6f, player.transform, out _), "반대 방향은 안 맞는다");

            for (int i = 0; i < _settings.WritingWipeStrokes - 1; i++)
                Assert.IsTrue(_ghost.ServerTryWipe(id, eye, aim, 6f, player.transform));
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out writing));
            Assert.AreEqual(1, writing.WipeRemaining, "아직 한 번 남았다");
            Assert.IsTrue(_ghost.ServerTryWipe(id, eye, aim, 6f, player.transform));
            Assert.AreEqual(0, _ghost.InkerWritingCount, "다 닦으면 사라진다");

            TurnPlayer(player, 0f, 180f, 0.4f);
            Assert.AreEqual(before, player.Sanity, "닦았으면 명령을 무시해도 된다");
        }

        [Test]
        public void 문틈_귀신과_눈이_마주치면_5_문_글씨를_발견하면_3이_깎이고_문은_닫힌_채로_풀린다()
        {
            DoorInteractable door = SpawnDoor(new Vector3(0f, 0f, 4f));
            SanityNetworkState player = SpawnPlayer(new Vector3(0.5f, 0f, 0f), 0f, _network.LocalClientId);
            int before = player.Sanity;

            StartPhenomenon(InkerPhenomenon.DoorPeek);
            Assert.AreEqual(InkerStep.DoorOpening, _ghost.InkerSequence.Step);
            Assert.IsTrue(door.IsGhostHeld, "현상 중에는 문을 여닫을 수 없다");
            Assert.IsFalse(door.IsOpen);

            Tick(3.05f);
            Assert.AreEqual(InkerStep.DoorWatching, _ghost.InkerSequence.Step);
            Tick(0.4f);
            Assert.AreEqual(InkerStep.DoorTilt, _ghost.InkerSequence.Step, "정면 15° 안에 0.3초 → 눈 맞춤");
            Assert.AreEqual(before - 5, player.Sanity);

            Tick(1.05f);
            Assert.AreEqual(InkerStep.DoorScratch, _ghost.InkerSequence.Step);
            Assert.AreEqual(1, _ghost.InkerWritingCount, "긁는 소리와 함께 글씨가 나타난다");
            Assert.IsTrue(door.IsGhostHeld);

            Tick(2.2f);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning);
            Assert.IsFalse(door.IsGhostHeld, "끝나면 문을 놓는다");
            Assert.IsFalse(door.IsOpen, "문은 닫힌 채로 남는다");
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out InkerWritingState writing));
            Assert.AreEqual("I SEE YOU", writing.Text);
            Assert.AreEqual(door.NetworkObjectId, writing.AnchorId, "문에 붙은 글씨");
            Assert.IsTrue(writing.Complete);

            Tick(0.2f);
            Assert.AreEqual(before - 5 - 3, player.Sanity, "문 앞 글씨 발견 −3 (한 번만)");
            Tick(0.5f);
            Assert.AreEqual(before - 5 - 3, player.Sanity);

            TurnPlayer(player, 0f, 180f, 0.4f);
            Assert.AreEqual(before - 5 - 3, player.Sanity, "\"I SEE YOU\" 는 금지하는 행동이 없다(IK-3 임시)");
            Assert.AreEqual(1, _ghost.InkerWritingCount);
        }

        [Test]
        public void 문틈_현상_중_어택이_시작되면_귀신과_글씨를_지우고_문을_닫힌_채로_놓는다()
        {
            DoorInteractable door = SpawnDoor(new Vector3(0f, 0f, 4f));
            SpawnPlayer(new Vector3(0.5f, 0f, 0f), 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.DoorPeek);
            Tick(1f);
            Tick(Dt, GhostPhase.Attack);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning, "현상 1 — 귀신 제거");
            Assert.IsFalse(door.IsGhostHeld);
            Assert.IsFalse(door.IsOpen, "문을 원래 닫힌 상태로");
            Assert.AreEqual(0, _ghost.InkerWritingCount);
        }

        [Test]
        public void 벽_글씨_중_어택이_시작되면_다_쓰기_전엔_지우고_다_썼으면_남긴다()
        {
            CreateWall();
            SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            Tick(1f);
            Tick(Dt, GhostPhase.Attack);
            Assert.AreEqual(0, _ghost.InkerWritingCount, "현상 2 — 다 쓰기 전이면 쓰던 글씨 제거");

            StartPhenomenon(InkerPhenomenon.WallWriting);
            Tick(4.1f);
            Tick(Dt, GhostPhase.Attack);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning);
            Assert.AreEqual(1, _ghost.InkerWritingCount, "현상 2 — 다 쓴 뒤면 글씨 유지");
        }

        [Test]
        public void 주기마다_확률로_발동하고_최대_횟수를_넘기지_않으며_어택_중에는_나오지_않는다()
        {
            CreateWall();
            SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            Set(_settings, "_inkerCheckInterval", 1f);
            Set(_settings, "_doorPeekChance", 0f);
            Set(_settings, "_wallWritingChance", 1f);
            Set(_settings, "_wallWritingMaxCount", 1);
            Set(_ghost, "_inkerCheckRemaining", 0.5f);

            Tick(2f, GhostPhase.Attack);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning, "어택 중에는 판정하지 않는다");

            Tick(0.6f);
            Assert.IsTrue(_ghost.InkerSequence.IsRunning, "집 안 플레이어 앞 벽에 쓰기 시작");
            Assert.AreEqual(InkerPhenomenon.WallWriting, _ghost.InkerSequence.Kind);
            Assert.AreEqual(1, _ghost.InkerWallWritingCount);

            Tick(9.5f);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning);
            Tick(3f);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning, "최대 횟수(1)를 채우면 더 나오지 않는다");
            Assert.AreEqual(1, _ghost.InkerWallWritingCount);
        }

        [Test]
        public void 문_열지_마를_본_플레이어가_문을_열면_위반이고_가구를_만지는_것은_괜찮다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            ForceCommand(InkerCommand.DontOpenDoor);
            Tick(4.1f);
            Tick(0.2f);
            int before = player.Sanity;

            FurnitureGrabTarget.ServerNotifyTouched(null, _network.LocalClientId);
            Tick(0.1f);
            Assert.AreEqual(before, player.Sanity, "다른 명령의 행동은 위반이 아니다");
            Assert.AreEqual(1, _ghost.InkerWritingCount);

            DoorInteractable.ServerNotifyOpenedByPlayer(null, _network.LocalClientId);
            Tick(Dt);
            Assert.AreEqual(before - 10, player.Sanity, "문을 열면 −10");
            Assert.AreEqual(0, _ghost.InkerWritingCount, "위반하면 글씨는 사라진다");
            Assert.AreEqual(1, _ghost.InkerViolationCount);
        }

        [Test]
        public void 가구_만지지_마를_본_플레이어가_가구를_잡으면_위반이다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            ForceCommand(InkerCommand.DontTouchFurniture);
            Tick(4.1f);
            Tick(0.2f);
            int before = player.Sanity;

            DoorInteractable.ServerNotifyOpenedByPlayer(null, _network.LocalClientId);
            Tick(0.1f);
            Assert.AreEqual(before, player.Sanity, "문을 여는 것은 이 명령 위반이 아니다");

            FurnitureGrabTarget.ServerNotifyTouched(null, _network.LocalClientId);
            Tick(Dt);
            Assert.AreEqual(before - 10, player.Sanity, "가구를 만지면 −10");
            Assert.AreEqual(0, _ghost.InkerWritingCount);
        }

        [Test]
        public void 글씨를_보기_전에_문을_연_것은_위반이_아니다()
        {
            CreateWall();
            SanityNetworkState player = SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            ForceCommand(InkerCommand.DontOpenDoor);
            player.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Tick(4.1f);
            int before = player.Sanity;

            // 벽을 등진 채 문을 열고, 그 뒤에 돌아서 글씨를 처음 본다.
            DoorInteractable.ServerNotifyOpenedByPlayer(null, _network.LocalClientId);
            Tick(0.1f);
            player.transform.rotation = Quaternion.identity;
            Tick(0.2f);
            Assert.AreEqual(before, player.Sanity, "보기 전에 한 행동은 세지 않는다");
            Assert.AreEqual(1, _ghost.InkerWritingCount);

            DoorInteractable.ServerNotifyOpenedByPlayer(null, _network.LocalClientId);
            Tick(Dt);
            Assert.AreEqual(before - 10, player.Sanity, "본 뒤에 열면 위반");
        }

        [Test]
        public void 인커는_기존_공통_초자연현상이_없다()
        {
            // 사용자 결정(2026-11-15): 인커는 자기 현상(문틈 응시·벽 글씨)만 갖는다.
            Assert.IsFalse(_ghost.HasClassicPhenomena);
            Assert.IsNull(Get(_ghost, "_phenomena"), "공통 현상 디렉터를 만들지 않는다");
            Set(_ghost, "_serverReady", true);
            try
            {
                Assert.AreEqual(GhostPhenomenonKind.None, _ghost.ServerForcePhenomenon());
                Assert.AreEqual(GhostPhenomenonKind.None, _ghost.ServerRunPhenomenon(GhostPhenomenonKind.LightFlicker),
                    "Hub 강제 실행도 막힌다");
                StringAssert.StartsWith("없음", _ghost.PhenomenonSummary);
            }
            finally
            {
                Set(_ghost, "_serverReady", false);
            }
        }

        [Test]
        public void 벽_글씨는_귀신_모습_없이_글씨만_저절로_써지고_다_쓰면_끝난다()
        {
            // 사용자 요청 2026-11-16 — 귀신이 쓰고 돌아보는 모습 없이 허공이 쓰는 느낌.
            SetBodyTemplate();
            CreateWall();
            SpawnPlayer(Vector3.zero, 0f, _network.LocalClientId);
            StartPhenomenon(InkerPhenomenon.WallWriting);
            Assert.AreEqual(InkerStep.WallWriting, _ghost.InkerSequence.Step);

            TickVisual();
            Assert.IsFalse(PuppetVisible(), "쓰는 동안 귀신 몸이 없다");
            Assert.AreEqual(1, _ghost.InkerWritingCount, "글씨는 써진다");
            Assert.IsNotNull(Get(_ghost, "_inkerLoopAudio") as AudioSource, "쓰는 소리는 난다");

            Tick(2f);
            TickVisual();
            Assert.AreEqual(InkerStep.WallWriting, _ghost.InkerSequence.Step);
            Assert.IsFalse(PuppetVisible(), "쓰는 중간에도 없다");

            Tick(2.1f);
            Assert.IsFalse(_ghost.InkerSequence.IsRunning, "다 쓰면 바로 끝난다");
            Assert.IsTrue(_ghost.TryGetInkerWriting(0, out InkerWritingState writing));
            Assert.IsTrue(writing.Complete, "명령 글씨로 남는다");
            TickVisual();
            Assert.IsFalse(PuppetVisible(), "다 쓴 뒤에도 귀신이 나타나 돌아보지 않는다");
            Assert.IsNull(Get(_ghost, "_inkerLoopAudio") as AudioSource, "쓰는 소리가 멈춘다");
        }

        [Test]
        public void 위반_갑툭튀_귀신은_스스로_빛나고_얼굴_앞에_불을_달며_벽에_묻히지_않는다()
        {
            // 사용자 요청 2026-11-16 — 어두운 집에서도 갑툭튀가 확실히 보여야 한다.
            SetBodyTemplate();
            CreateWall(); // 벽 앞면 z = 3.9
            GameObject cameraObject = Create("InkerTestCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.identity);

            Set(_ghost, "_inkerScareRemaining", 0.8f);
            Invoke("TickInkerScare");
            var scare = (InkerPuppetView)Get(_ghost, "_inkerScarePuppet");
            Assert.IsNotNull(scare);
            Assert.IsTrue(scare.IsGlowing, "갑툭튀 인형은 발광한다");
            Assert.IsNotNull(scare.GlowLight, "얼굴 앞 점광원");
            Assert.IsTrue(scare.GlowLight.enabled);
            Assert.Greater(scare.GlowLight.intensity, 0f);
            Assert.IsTrue(AnyRendererEnabled(scare));
            foreach (Renderer renderer in scare.GetComponentsInChildren<Renderer>())
            {
                Material material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty("_EmissionColor"))
                    continue;
                Assert.IsTrue(material.IsKeywordEnabled("_EMISSION"), "발광 키워드가 켜진다(인형 전용 사본)");
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.Greater(block.GetColor("_EmissionColor").maxColorComponent, 1f, "HDR 발광");
            }

            Assert.AreEqual(_settings.JumpScareDistance, PlanarDistance(scare.transform.position, cameraObject.transform.position),
                0.05f, "벽이 멀면 기본 거리");

            // 벽 0.6m 앞에서 어기면 벽에 묻히지 않게 벽 앞(0.6 − 0.25)에 띄운다.
            cameraObject.transform.position = new Vector3(0f, 1.6f, 3.3f);
            Set(_ghost, "_inkerScareRemaining", 0.8f);
            Invoke("TickInkerScare");
            Assert.AreEqual(0.35f, PlanarDistance(scare.transform.position, cameraObject.transform.position), 0.05f,
                "벽이 가까우면 그 앞");
        }

        // ───────── 도우미 ─────────

        /// <summary>인형이 복제할 귀신 몸 — 충돌체 없는 큐브(기본 머티리얼은 발광 키워드가 꺼져 있다).</summary>
        private void SetBodyTemplate()
        {
            GameObject body = Create("InkerTestBody");
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(body.transform, false);
            cube.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.SetActive(false);
            Set(_ghost, "_body", body);
        }

        private void TickVisual() => Invoke("TickInkerVisual");

        private bool PuppetVisible()
        {
            var puppet = Get(_ghost, "_inkerPuppet") as InkerPuppetView;
            return puppet != null && AnyRendererEnabled(puppet);
        }

        private void Invoke(string method)
            => typeof(GhostPrototypeController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_ghost, null);

        private static bool AnyRendererEnabled(InkerPuppetView puppet)
        {
            foreach (Renderer renderer in puppet.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled)
                    return true;
            }

            return false;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void StartPhenomenon(InkerPhenomenon kind)
        {
            Set(_ghost, "_serverReady", true);
            string result = _ghost.ServerForceInkerPhenomenon(kind);
            Set(_ghost, "_serverReady", false);
            StringAssert.StartsWith("인커 현상 시작", result, result);
        }

        /// <summary>현상 2가 고른 명령을 시험할 명령으로 바꾼다(무작위 선택 고정).</summary>
        private void ForceCommand(InkerCommand command)
        {
            var list = (NetworkList<InkerWritingState>)Get(_ghost, "_inkerWritings");
            InkerWritingState writing = list[0];
            writing.Command = command;
            list[0] = writing;
        }

        private void Tick(float seconds, GhostPhase phase = GhostPhase.Active)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            var players = (SanityNetworkState[])Get(_ghost, "_players");
            MethodInfo tick = typeof(GhostPrototypeController).GetMethod("ServerTickInker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < ticks; i++)
            {
                int count = _team.CopyPlayerStates(players);
                tick.Invoke(_ghost, new object[] { Dt, phase, count });
            }
        }

        private void TurnPlayer(SanityNetworkState player, float fromYaw, float toYaw, float seconds)
        {
            int ticks = Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));
            for (int i = 1; i <= ticks; i++)
            {
                player.transform.rotation = Quaternion.Euler(0f, Mathf.Lerp(fromYaw, toYaw, i / (float)ticks), 0f);
                Tick(Dt);
            }
        }

        private void CreateWall()
        {
            GameObject wall = Create("InkerTestWall");
            wall.transform.position = new Vector3(0f, 1.5f, 4f);
            wall.AddComponent<BoxCollider>().size = new Vector3(10f, 3f, 0.2f);
            Physics.SyncTransforms();
        }

        private DoorInteractable SpawnDoor(Vector3 hinge)
        {
            GameObject door = Create("InkerTestDoor");
            door.SetActive(false);
            door.transform.position = hinge;
            var networkObject = door.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            DoorInteractable interactable = door.AddComponent<DoorInteractable>();
            Set(interactable, "_startsOpen", false);
            Set(interactable, "_closedYaw", 0f);
            Set(interactable, "_openYaw", 90f);
            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(door.transform, false);
            leaf.transform.localPosition = new Vector3(0.5f, 1.15f, 0f);
            leaf.AddComponent<BoxCollider>().size = new Vector3(1f, 2.3f, 0.06f);
            door.SetActive(true);
            networkObject.Spawn();
            Physics.SyncTransforms();
            return interactable;
        }

        private SanityNetworkState SpawnPlayer(Vector3 position, float yaw, ulong owner)
        {
            GameObject player = Create("InkerTestPlayer");
            player.SetActive(false);
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var networkObject = player.AddComponent<NetworkObject>();
            AssignHash(networkObject);
            var state = player.AddComponent<SanityNetworkState>();
            Set(state, "_settings", _sanitySettings);
            player.SetActive(true);
            networkObject.SpawnWithOwnership(owner);
            Assert.IsTrue(state.HasSanity);
            return state;
        }

        private GameObject Create(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }

        private void AssignHash(NetworkObject value)
            => typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, _hash++);

        private static object Get(object target, string name)
            => Field(target.GetType(), name).GetValue(target);

        private static void Set(object target, string name, object value)
            => Field(target.GetType(), name).SetValue(target, value);

        private static FieldInfo Field(System.Type type, string name)
        {
            for (System.Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                    return field;
            }

            throw new MissingFieldException(type.Name, name);
        }

        private sealed class MissingFieldException : System.Exception
        {
            public MissingFieldException(string type, string field) : base($"{type}.{field}") { }
        }
    }
}
