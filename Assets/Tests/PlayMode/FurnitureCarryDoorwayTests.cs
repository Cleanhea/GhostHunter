using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 2인 운반 추종(<see cref="FurnitureCarrySession"/>, throw-system.md §3)을 문틀 모형에서 물리로 직접 돌린다.
    /// 네트워크 없이 두 홀더의 눈·조준을 스크립트로 움직이고 <see cref="Physics.Simulate"/>로 스텝을 진행한다 —
    /// 실제 2인 접속의 지연·체감은 덮지 않는다.
    /// </summary>
    public sealed class FurnitureCarryDoorwayTests
    {
        private const float Step = 0.02f;
        private const float WalkSpeed = 1.5f;
        private const float EyeHeight = 1.2f;
        private const float HandReach = 1.2f;
        private const float SofaWidth = 0.7f;
        private const float SofaLength = 1.8f;
        private const float CarryHeight = 0.9f;

        private readonly List<Object> _created = new();
        private SimulationMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            Physics.simulationMode = _previousMode;
        }

        [Test]
        public void 앞뒤로_서서_가운데로_걸으면_긴_가구가_문을_지난다()
        {
            Carry carry = CreateCarry(0f, CreateSettings(squeeze: true));

            carry.Walk(4f);

            Assert.Greater(carry.Body.position.z, 1.2f, $"소파 전체가 문(z=0)을 지나야 합니다. {carry}");
        }

        [Test]
        public void 문틀에_걸리면_끼임_보조가_옆으로_비켜_통과시킨다()
        {
            // 두 사람이 문 가운데에서 0.35m 비켜 걸으면 소파 오른쪽 0.2m 가 문틀 끝면에 정면으로 걸린다.
            Carry carry = CreateCarry(0.35f, CreateSettings(squeeze: true));

            carry.Walk(4f);

            Assert.Greater(carry.Body.position.z, 1.2f, $"끼임 보조로 문을 지나야 합니다. {carry}");
            Assert.Less(carry.MinAssistX, -0.05f, $"도중에 왼쪽(문 가운데 쪽)으로 비켜야 합니다. {carry}");
        }

        [Test]
        public void 끼임_보조를_끄면_같은_상황에서_문틀에_걸려_멈춘다()
        {
            Carry carry = CreateCarry(0.35f, CreateSettings(squeeze: false));

            carry.Walk(4f);

            Assert.Less(carry.Body.position.z, 0f, $"보조 없이는 문틀 끝면에 막혀야 대조군이 됩니다. {carry}");
        }

        [Test]
        public void 문을_지나고_자리가_비면_끼임_보조를_되돌린다()
        {
            Carry carry = CreateCarry(0.35f, CreateSettings(squeeze: true));

            carry.Walk(5f);

            Assert.Greater(carry.Body.position.z, 2f, $"{carry}");
            Assert.Less(carry.Session.AssistOffset.magnitude, 0.001f, $"문을 지난 뒤에는 옆 이동이 0으로 돌아와야 합니다. {carry}");
            Assert.AreEqual(0.35f, carry.Body.position.x, 0.03f, "다시 두 사람이 걷는 선을 따라가야 합니다.");
        }

        [Test]
        public void 앞사람이_옆으로_돌아_서면_가구도_그쪽으로_돈다()
        {
            Carry carry = CreateCarry(0f, CreateSettings(squeeze: true), doorway: false);

            // 앞사람(B)이 뒷사람 기준 오른쪽(+x)으로 반원을 돌아가 두 손이 x 축으로 늘어선다.
            carry.Simulate(1.5f, t =>
            {
                float angle = Mathf.Clamp01(t / 1f) * 90f;
                Vector3 center = carry.StartA;
                Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * (carry.StartB - carry.StartA);
                carry.EyeB = center + offset;
            });

            Vector3 longAxis = carry.Body.rotation * Vector3.forward;
            Assert.Greater(Mathf.Abs(longAxis.x), 0.98f, "소파 긴 쪽이 x 축을 향해야 합니다.");
        }

        private FurnitureThrowSettings CreateSettings(bool squeeze)
        {
            var settings = ScriptableObject.CreateInstance<FurnitureThrowSettings>();
            _created.Add(settings);
            if (!squeeze)
            {
                FieldInfo field = typeof(FurnitureThrowSettings).GetField(
                    "_squeezeTriggerDistance", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(field, "FurnitureThrowSettings._squeezeTriggerDistance 가 없습니다.");
                field.SetValue(settings, 0f);
            }

            return settings;
        }

        private Carry CreateCarry(float laneX, FurnitureThrowSettings settings, bool doorway = true)
        {
            CreateBox("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(12f, 0.5f, 12f));
            if (doorway)
            {
                // 폭 1.0m 출입구(x −0.5~0.5), 벽 두께 0.2m(z −0.1~0.1).
                CreateBox("LeftJamb", new Vector3(-2.5f, 1.2f, 0f), new Vector3(4f, 2.4f, 0.2f));
                CreateBox("RightJamb", new Vector3(2.5f, 1.2f, 0f), new Vector3(4f, 2.4f, 0.2f));
            }

            var sofa = new GameObject("Sofa");
            _created.Add(sofa);
            sofa.transform.position = new Vector3(laneX, CarryHeight, -2.5f);
            BoxCollider box = sofa.AddComponent<BoxCollider>();
            box.size = new Vector3(SofaWidth, 0.5f, SofaLength);
            var material = new PhysicsMaterial("CarriedFurniture")
            {
                dynamicFriction = settings.CarryFriction,
                staticFriction = settings.CarryFriction,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            _created.Add(material);
            box.sharedMaterial = material;

            Rigidbody body = sofa.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            CarryContactRecorder recorder = sofa.AddComponent<CarryContactRecorder>();
            Physics.SyncTransforms();

            var colliders = new Collider[] { box };
            var probe = new FurnitureClearanceProbe(body, colliders, Physics.DefaultRaycastLayers);
            var session = new FurnitureCarrySession(body, probe, null);
            return new Carry(body, recorder, session, settings);
        }

        private void CreateBox(string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            _created.Add(box);
            box.transform.position = position;
            box.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>뒷사람 A·앞사람 B 가 소파 양 끝을 잡고 +z 로 걷는 장면.</summary>
        private sealed class Carry
        {
            private readonly CarryContactRecorder _recorder;
            private readonly Vector3 _gripA;
            private readonly Vector3 _gripB;
            private Quaternion _heldRotation;
            private float _time;

            public Carry(Rigidbody body, CarryContactRecorder recorder, FurnitureCarrySession session,
                FurnitureThrowSettings settings)
            {
                Body = body;
                _recorder = recorder;
                Session = session;
                Vector3 center = body.position;
                _gripA = center + Vector3.back * (SofaLength * 0.5f);
                _gripB = center + Vector3.forward * (SofaLength * 0.5f);
                StartA = new Vector3(center.x, EyeHeight, _gripA.z - HandReach);
                StartB = new Vector3(center.x, EyeHeight, _gripB.z + HandReach);
                EyeA = StartA;
                EyeB = StartB;
                _heldRotation = body.rotation;
                session.Begin(settings, EyeA, (_gripA - EyeA).normalized, _gripA,
                    EyeB, (_gripB - EyeB).normalized, _gripB);
            }

            public Rigidbody Body { get; }

            public override string ToString() =>
                $"pos={Body.position:F3} euler={Body.rotation.eulerAngles:F1} assist={Session.AssistOffset:F3}/{Session.AssistYaw:F1} minAssistX={MinAssistX:F2} t={_time:F2}";
            public FurnitureCarrySession Session { get; }
            public Vector3 StartA { get; }
            public Vector3 StartB { get; }
            public Vector3 EyeA { get; set; }
            public Vector3 EyeB { get; set; }

            /// <summary>운반 도중 끼임 보조 옆 이동 x 의 최솟값.</summary>
            public float MinAssistX { get; private set; }

            /// <summary>두 사람이 같은 속도로 +z 로 걷는다. 시선은 잡은 끝을 계속 본다(잡은 거리 유지).</summary>
            public void Walk(float seconds)
            {
                Simulate(seconds, t =>
                {
                    Vector3 travel = Vector3.forward * (WalkSpeed * t);
                    EyeA = StartA + travel;
                    EyeB = StartB + travel;
                });
            }

            /// <summary>매 스텝 <paramref name="move"/>로 눈 위치를 정하고, 각자 상대 쪽(소파)을 향한 조준으로 한 스텝 진행한다.</summary>
            public void Simulate(float seconds, System.Action<float> move)
            {
                Vector3 handOffsetA = _gripA - StartA;
                Vector3 handOffsetB = _gripB - StartB;
                int steps = Mathf.RoundToInt(seconds / Step);
                for (int i = 0; i < steps; i++)
                {
                    _time += Step;
                    move(_time);

                    // 각자 상대를 바라보는 방향으로 잡은 손 오프셋을 돌려 조준을 만든다.
                    Vector3 directionA = Aim(EyeA, EyeB, handOffsetA, Vector3.forward);
                    Vector3 directionB = Aim(EyeB, EyeA, handOffsetB, Vector3.back);
                    Session.Step(Step, _time, EyeA, directionA, EyeB, directionB, _heldRotation, _recorder.Consume());
                    Physics.Simulate(Step);
                    MinAssistX = Mathf.Min(MinAssistX, Session.AssistOffset.x);
                }
            }

            private static Vector3 Aim(Vector3 eye, Vector3 partner, Vector3 startOffset, Vector3 startFacing)
            {
                Vector3 facing = new(partner.x - eye.x, 0f, partner.z - eye.z);
                if (facing.sqrMagnitude < 0.0001f)
                    facing = startFacing;

                Quaternion turn = Quaternion.FromToRotation(startFacing, facing.normalized);
                return (turn * startOffset).normalized;
            }
        }
    }
}
