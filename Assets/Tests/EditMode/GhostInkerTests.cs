using System.Linq;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// '명령(인커)' 귀신(project/ghost-inker.md)의 순수 로직과 배선. 서버 흐름(발동·눈 맞춤·위반·닦기)은
    /// PlayMode <c>GhostInkerFlowTests</c> 가 실제 컨트롤러로 확인한다.
    /// </summary>
    public sealed class GhostInkerTests
    {
        private const string InkerSettingsPath = "Assets/Settings/Gameplay/GhostPrototypeSettings_Inker.asset";
        private const string DefaultSettingsPath = "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset";
        private const string InkerPrefabPath = "Assets/Prefabs/Ghost/Ghost_Inker.prefab";
        private const string PrototypePrefabPath = "Assets/Prefabs/Ghost/Ghost_Prototype.prefab";
        private const string TutorialPrefabPath = "Assets/Prefabs/Ghost/Ghost_Tutorial.prefab";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string Stage1Path = "Assets/Scenes/Stage1.unity";

        // ───────── 진행표 ─────────

        [Test]
        public void 문틈_현상은_눈이_마주치면_고개를_기울이고_원문_시간대로_끝난다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartDoorPeek(InkerTimings.Original, closeDoorFirst: false);
            Assert.AreEqual(InkerStep.DoorOpening, sequence.Step, "① 20° 열림부터");

            Assert.IsTrue(sequence.Tick(2f, false, false));
            Assert.AreEqual(InkerStep.DoorLean, sequence.Step, "2초 뒤 ② 얼굴 내밀기");
            Assert.IsTrue(sequence.Tick(1f, false, false));
            Assert.AreEqual(InkerStep.DoorWatching, sequence.Step, "1초 뒤 ③ 대기");
            Assert.IsFalse(sequence.Tick(2f, false, false), "눈이 안 마주치면 대기 유지");

            Assert.IsTrue(sequence.Tick(0.1f, true, false));
            Assert.AreEqual(InkerStep.DoorTilt, sequence.Step, "눈이 마주치면 ④ 고개 기울이기");
            Assert.IsTrue(sequence.EyeContactMade);
            Assert.IsTrue(sequence.Tick(0.5f, false, false));
            Assert.AreEqual(InkerStep.DoorSlam, sequence.Step, "0.5초 뒤 ⑤ 숨으며 문이 닫힘");
            Assert.IsTrue(sequence.Tick(0.5f, false, false));
            Assert.AreEqual(InkerStep.DoorScratch, sequence.Step, "0.5초 뒤 ⑥ 긁는 소리·글씨");
            Assert.AreEqual(0f, sequence.WritingProgress, 1e-4f);
            sequence.Tick(1f, false, false);
            Assert.AreEqual(0.5f, sequence.WritingProgress, 1e-4f, "글씨는 2초에 걸쳐 나타난다");
            Assert.IsTrue(sequence.Tick(1f, false, false));
            Assert.AreEqual(InkerStep.Done, sequence.Step);
            Assert.IsFalse(sequence.IsRunning);
        }

        [Test]
        public void 문틈_현상은_5초_안에_눈이_안_마주치면_고개를_기울이지_않고_닫힌다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartDoorPeek(InkerTimings.Original, false);
            sequence.Tick(2f, false, false);
            sequence.Tick(1f, false, false);
            Assert.AreEqual(InkerStep.DoorWatching, sequence.Step);

            Assert.IsFalse(sequence.Tick(4.9f, false, false));
            Assert.IsTrue(sequence.Tick(0.1f, false, false));
            Assert.AreEqual(InkerStep.DoorSlam, sequence.Step, "④를 건너뛰고 ⑤로");
            Assert.IsFalse(sequence.EyeContactMade);
        }

        [Test]
        public void 강제_실행은_열린_문을_먼저_닫은_뒤_연다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartDoorPeek(InkerTimings.Original, closeDoorFirst: true);
            Assert.AreEqual(InkerStep.DoorPrepare, sequence.Step);
            Assert.IsTrue(sequence.Tick(0.6f, false, false));
            Assert.AreEqual(InkerStep.DoorOpening, sequence.Step);
        }

        [Test]
        public void 눈_맞춤은_대기_단계에서만_본다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartDoorPeek(InkerTimings.Original, false);
            sequence.Tick(0.5f, true, false);
            Assert.AreEqual(InkerStep.DoorOpening, sequence.Step, "문이 열리는 중에는 눈 맞춤을 보지 않는다");
            Assert.IsFalse(sequence.EyeContactMade);
        }

        [Test]
        public void 벽_글씨는_4초_쓰고_목을_돌린_뒤_3초_응시하고_1초에_사라진다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartWallWriting(InkerTimings.Original);
            Assert.AreEqual(InkerStep.WallWriting, sequence.Step);

            sequence.Tick(2f, false, false);
            Assert.AreEqual(0.5f, sequence.WritingProgress, 1e-4f, "손가락 이동에 맞춰 차례로 나타난다");
            Assert.IsTrue(sequence.Tick(2f, false, false));
            Assert.AreEqual(InkerStep.WallTurnHead, sequence.Step);
            Assert.AreEqual(1f, sequence.WritingProgress, "다 쓰면 명령 글씨로 활성화된다");
            Assert.IsTrue(sequence.Tick(1f, false, false));
            Assert.AreEqual(InkerStep.WallStare, sequence.Step);
            Assert.IsTrue(sequence.Tick(3f, false, false));
            Assert.AreEqual(InkerStep.WallVanish, sequence.Step);
            Assert.IsTrue(sequence.Tick(1f, false, false));
            Assert.AreEqual(InkerStep.Done, sequence.Step);
        }

        [Test]
        public void 쓰는_중_2m_안으로_다가오면_즉시_완성하고_목을_돌린다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartWallWriting(InkerTimings.Original);
            sequence.Tick(1f, false, false);
            Assert.AreEqual(0.25f, sequence.WritingProgress, 1e-4f);

            Assert.IsTrue(sequence.Tick(0.01f, false, true));
            Assert.AreEqual(InkerStep.WallTurnHead, sequence.Step);
            Assert.IsTrue(sequence.Rushed);
            Assert.AreEqual(1f, sequence.WritingProgress);
        }

        [Test]
        public void 긴_프레임은_남은_시간을_다음_단계로_넘기고_취소하면_멈춘다()
        {
            var sequence = new GhostInkerSequence();
            sequence.StartWallWriting(InkerTimings.Original);
            sequence.Tick(4.5f, false, false);
            Assert.AreEqual(InkerStep.WallTurnHead, sequence.Step);
            Assert.AreEqual(0.5f, sequence.StepElapsed, 1e-4f);

            sequence.Cancel();
            Assert.IsFalse(sequence.IsRunning);
            Assert.AreEqual(InkerStep.None, sequence.Step);
            Assert.IsFalse(sequence.Tick(10f, false, false));
        }

        // ───────── 뒤돌기 판정 ─────────

        [Test]
        public void 일초_안에_135도_이상_돌면_뒤돈_것이다()
        {
            var detector = new InkerTurnAroundDetector();
            Assert.IsFalse(detector.Sample(0f, 0f, 1f, 135f));
            Assert.IsFalse(detector.Sample(0.3f, 70f, 1f, 135f));
            Assert.IsTrue(detector.Sample(0.6f, 140f, 1f, 135f));
        }

        [Test]
        public void 천천히_둘러보거나_좌우로_흔드는_것은_뒤돈_것이_아니다()
        {
            var slow = new InkerTurnAroundDetector();
            for (int i = 0; i <= 20; i++)
                Assert.IsFalse(slow.Sample(i * 0.5f, i * 20f, 1f, 135f), $"천천히 {i * 20}°");

            var shake = new InkerTurnAroundDetector();
            for (int i = 0; i < 20; i++)
                Assert.IsFalse(shake.Sample(i * 0.1f, i % 2 == 0 ? 60f : -60f, 1f, 135f), "±60° 흔들기");
        }

        [Test]
        public void 창_밖의_표본과_초기화_전_표본은_세지_않는다()
        {
            var detector = new InkerTurnAroundDetector();
            detector.Sample(0f, 0f, 1f, 135f);
            Assert.IsFalse(detector.Sample(1.5f, 170f, 1f, 135f), "1초 전보다 오래된 방향은 잊는다");

            detector.Reset();
            detector.Sample(2f, 0f, 1f, 135f);
            detector.Reset();
            Assert.IsFalse(detector.Sample(2.2f, 170f, 1f, 135f), "초기화하면(글씨 발견 순간) 이전 방향은 없다");
        }

        [Test]
        public void 뒤돌기_판정은_360도_경계를_넘어도_최소_각도로_잰다()
        {
            var detector = new InkerTurnAroundDetector();
            detector.Sample(0f, 350f, 1f, 135f);
            Assert.IsFalse(detector.Sample(0.3f, 120f, 1f, 135f), "350° → 120° 는 130°");
            Assert.IsTrue(detector.Sample(0.5f, 170f, 1f, 135f), "350° → 170° 는 180°");
        }

        // ───────── 글씨 기하·표시 ─────────

        [Test]
        public void 글씨_사각형은_앞면에서만_맞고_밖이나_멀면_안_맞는다()
        {
            var center = new Vector3(0f, 1.5f, 0f);
            Quaternion facingMinusZ = Quaternion.LookRotation(Vector3.back);

            Assert.IsTrue(InkerWritingGeometry.RaycastRect(new Vector3(0f, 1.5f, -2f), Vector3.forward, 3f,
                center, facingMinusZ, 1f, 0.3f, out float distance));
            Assert.AreEqual(2f, distance, 1e-4f);
            Assert.IsFalse(InkerWritingGeometry.RaycastRect(new Vector3(0f, 1.5f, 2f), Vector3.back, 3f,
                center, facingMinusZ, 1f, 0.3f, out _), "뒤(벽 속)에서는 안 맞는다");
            Assert.IsFalse(InkerWritingGeometry.RaycastRect(new Vector3(0.6f, 1.5f, -2f), Vector3.forward, 3f,
                center, facingMinusZ, 1f, 0.3f, out _), "폭 밖");
            Assert.IsFalse(InkerWritingGeometry.RaycastRect(new Vector3(0f, 1.5f, -2f), Vector3.forward, 1.5f,
                center, facingMinusZ, 1f, 0.3f, out _), "최대 거리 밖");
            Assert.IsTrue(InkerWritingGeometry.IsInFront(new Vector3(0f, 1.5f, -1f), center, facingMinusZ));
            Assert.IsFalse(InkerWritingGeometry.IsInFront(new Vector3(0f, 1.5f, 1f), center, facingMinusZ));
        }

        [Test]
        public void 문에_쓴_글씨는_문을_따라_돌고_문을_못_찾으면_숨는다()
        {
            var door = new GameObject("InkerTestDoor");
            try
            {
                door.transform.SetPositionAndRotation(new Vector3(5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                var writing = new InkerWritingState
                {
                    AnchorId = 7,
                    LocalPosition = new Vector3(0.5f, 1.4f, 0.05f),
                    LocalRotation = Quaternion.identity,
                };

                Assert.IsTrue(InkerWritingGeometry.TryGetWorldPose(writing, door.transform, out Vector3 position,
                    out Quaternion rotation));
                Assert.That(Vector3.Distance(door.transform.TransformPoint(writing.LocalPosition), position),
                    Is.LessThan(1e-4f));
                Assert.Greater(Vector3.Dot(rotation * Vector3.forward, Vector3.right), 0.999f);
                Assert.IsFalse(InkerWritingGeometry.TryGetWorldPose(writing, null, out _, out _));
            }
            finally
            {
                Object.DestroyImmediate(door);
            }
        }

        // 글씨 뷰(읽는 방향·쓰기 진행)는 InkerBloodWritingTests 가 피 글씨로 확인한다(2026-11-15 TextMesh → 피 획).

        [Test]
        public void 명령_문구는_원문_예시를_대문자로_쓴다()
        {
            Assert.AreEqual("DON'T TURN AROUND", InkerCommandText.Of(InkerCommand.DontTurnAround));
            Assert.AreEqual("DON'T JUMP", InkerCommandText.Of(InkerCommand.DontJump));
            Assert.AreEqual("I SEE YOU", InkerCommandText.Of(InkerCommand.None));
            // 사용자 요청 2026-11-15 — 문 열지 마·가구 만지지 마.
            Assert.AreEqual("DON'T OPEN THE DOOR", InkerCommandText.Of(InkerCommand.DontOpenDoor));
            Assert.AreEqual("DON'T TOUCH THE FURNITURE", InkerCommandText.Of(InkerCommand.DontTouchFurniture));
        }

        // ───────── 귀신 종류 고르기 ─────────

        [Test]
        public void 시작_랜덤은_후보에_넣은_종류만_고른다()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            var c = new GameObject("C");
            try
            {
                GhostTypeOption[] options =
                {
                    new() { Label = "A", Prefab = a, InRandomPool = true },
                    new() { Label = "B", Prefab = b, InRandomPool = false },
                    new() { Label = "C", Prefab = c, InRandomPool = true },
                };
                Assert.AreEqual(0, GhostTypePicker.PickRandom(options, 0f));
                Assert.AreEqual(2, GhostTypePicker.PickRandom(options, 0.5f));
                Assert.AreEqual(2, GhostTypePicker.PickRandom(options, 0.999f));
                Assert.AreEqual(2, GhostTypePicker.IndexOf(options, c));

                options[0].InRandomPool = false;
                options[2].InRandomPool = false;
                Assert.AreEqual(0, GhostTypePicker.PickRandom(options, 0.7f), "후보가 없으면 프리팹이 있는 첫 종류");
                Assert.AreEqual(-1, GhostTypePicker.PickRandom(new GhostTypeOption[2], 0.3f), "프리팹이 하나도 없으면 -1");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
        }

        // ───────── 에셋·배선 ─────────

        [Test]
        public void 인커_설정_에셋은_원문_수치를_쓴다()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GhostInkerSettings>(InkerSettingsPath);
            Assert.IsNotNull(settings, $"{InkerSettingsPath} 가 GhostInkerSettings 가 아니다");

            Assert.AreEqual(0.2f, settings.DoorPeekChance, 1e-4f, "현상 1 20%");
            Assert.AreEqual(3, settings.DoorPeekMaxCount);
            Assert.AreEqual(2f, settings.DoorPeekMinDistance);
            Assert.AreEqual(6f, settings.DoorPeekMaxDistance);
            Assert.AreEqual(1f, settings.DoorPeekClearRadius);
            Assert.AreEqual(20f, settings.DoorPeekOpenDegrees);
            Assert.AreEqual(30f, settings.DoorPeekTiltDegrees);
            Assert.AreEqual(15f, settings.EyeContactAngle);
            Assert.AreEqual(0.3f, settings.EyeContactSeconds, 1e-4f);
            Assert.AreEqual(5, settings.EyeContactSanityDecrease);
            Assert.AreEqual(3, settings.DoorWritingSanityDecrease);
            Assert.AreEqual(0.3f, settings.WallWritingChance, 1e-4f, "현상 2 30%");
            Assert.AreEqual(2, settings.WallWritingMaxCount);
            Assert.AreEqual(2f, settings.WallWritingMinDistance);
            Assert.AreEqual(7f, settings.WallWritingMaxDistance);
            Assert.AreEqual(2f, settings.WallRushDistance);
            Assert.AreEqual(10, settings.ViolationSanityDecrease, "명령 위반 10%(0~100 척도의 10)");
            Assert.AreEqual(4, settings.WallCommandCount, "뒤돌기·점프·문 열기·가구 만지기(2026-11-15)");
            Assert.IsNotNull(settings.WritingMaterial, "피 글씨 머티리얼");
            Assert.AreEqual(InkerWritingView.ShaderName, settings.WritingMaterial.shader.name);
            Assert.IsTrue(settings.AtmosphereEnabled, "현상 화면 연출");

            InkerTimings timings = settings.Timings;
            InkerTimings original = InkerTimings.Original;
            Assert.AreEqual(original.DoorOpen, timings.DoorOpen);
            Assert.AreEqual(original.DoorLean, timings.DoorLean);
            Assert.AreEqual(original.DoorWatch, timings.DoorWatch);
            Assert.AreEqual(original.DoorTilt, timings.DoorTilt);
            Assert.AreEqual(original.DoorSlam, timings.DoorSlam);
            Assert.AreEqual(original.DoorScratch, timings.DoorScratch);
            Assert.AreEqual(original.WallWrite, timings.WallWrite);
            Assert.AreEqual(original.WallTurnHead, timings.WallTurnHead);
            Assert.AreEqual(original.WallStare, timings.WallStare);
            Assert.AreEqual(original.WallVanish, timings.WallVanish);
        }

        [Test]
        public void 인커_설정은_기본_귀신_설정값을_그대로_물려받는다()
        {
            var inker = AssetDatabase.LoadAssetAtPath<GhostInkerSettings>(InkerSettingsPath);
            var basic = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(DefaultSettingsPath);
            Assert.IsNotNull(inker);
            Assert.IsNotNull(basic);
            Assert.IsNotInstanceOf<GhostInkerSettings>(basic, "기본 귀신 에셋은 인커가 아니다(ADR-0024)");

            var inkerObject = new SerializedObject(inker);
            SerializedProperty property = new SerializedObject(basic).GetIterator();
            int compared = 0;
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (property.propertyPath == "m_Script")
                    continue;
                SerializedProperty other = inkerObject.FindProperty(property.propertyPath);
                Assert.IsNotNull(other, property.propertyPath);
                Assert.IsTrue(SerializedProperty.DataEquals(property, other),
                    $"{property.propertyPath} 가 Default 와 다르다 — Stage1 기본 귀신과 같은 공통 수치를 써야 한다");
                compared++;
            }

            Assert.Greater(compared, 50);
        }

        [Test]
        public void Ghost_Inker_프리팹은_인커_설정과_고유한_네트워크_식별자를_쓰고_등록되어_있다()
        {
            var inker = AssetDatabase.LoadAssetAtPath<GameObject>(InkerPrefabPath);
            Assert.IsNotNull(inker, $"{InkerPrefabPath} 를 찾지 못했다");
            var controller = inker.GetComponent<GhostPrototypeController>();
            Assert.IsNotNull(controller);
            Object settings = new SerializedObject(controller).FindProperty("_settings").objectReferenceValue;
            Assert.IsInstanceOf<GhostInkerSettings>(settings);

            uint hash = Hash(inker);
            Assert.AreNotEqual(0u, hash);
            Assert.AreNotEqual(Hash(AssetDatabase.LoadAssetAtPath<GameObject>(PrototypePrefabPath)), hash);
            Assert.AreNotEqual(Hash(AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPrefabPath)), hash);
            Assert.IsFalse(new SerializedObject(inker.GetComponent<NetworkObject>())
                .FindProperty("m_InScenePlaced").boolValue);

            foreach (Transform child in inker.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(GhostHunter.Core.GameLayers.GhostPrototype, child.gameObject.layer, child.name);

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsTrue(list.PrefabList.Any(entry => entry.Prefab == inker), "DefaultNetworkPrefabs 에 Ghost_Inker 가 없다");
        }

        [Test]
        public void Stage1_스포너는_기본과_인커를_시작_랜덤_후보로_두고_박치기는_Hub_선택만_된다()
        {
            Scene scene = SceneManager.GetSceneByPath(Stage1Path);
            bool openedHere = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(Stage1Path, OpenSceneMode.Additive);
                openedHere = true;
            }

            try
            {
                GhostPrototypeSpawner spawner = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GhostPrototypeSpawner>(true))
                    .Single();
                var serialized = new SerializedObject(spawner);
                Assert.IsTrue(serialized.FindProperty("_randomizeInitialType").boolValue);
                SerializedProperty types = serialized.FindProperty("_ghostTypes");
                Assert.AreEqual(3, types.arraySize);

                var inker = AssetDatabase.LoadAssetAtPath<GameObject>(InkerPrefabPath);
                var prototype = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypePrefabPath);
                var tutorial = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPrefabPath);
                AssertType(types.GetArrayElementAtIndex(0), prototype, true);
                AssertType(types.GetArrayElementAtIndex(1), inker, true);
                AssertType(types.GetArrayElementAtIndex(2), tutorial, false);
                Assert.AreEqual(prototype, serialized.FindProperty("_ghostPrefab").objectReferenceValue,
                    "기본 귀신 프리팹은 그대로");
                // Hub 이상현상 — 지금 귀신이 인커가 아니어도 종류 목록에서 인커를 찾아 교체 후 시험할 수 있다.
                Assert.IsFalse(spawner.HasInkerGhost);
                Assert.IsTrue(spawner.CanTestInker, "종류 목록에서 인커(GhostInkerSettings 프리팹)를 찾는다");
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertType(SerializedProperty option, GameObject prefab, bool inRandomPool)
        {
            Assert.AreEqual(prefab, option.FindPropertyRelative("Prefab").objectReferenceValue);
            Assert.AreEqual(inRandomPool, option.FindPropertyRelative("InRandomPool").boolValue);
            Assert.IsFalse(string.IsNullOrEmpty(option.FindPropertyRelative("Label").stringValue));
        }

        private static uint Hash(GameObject prefab)
            => (uint)new SerializedObject(prefab.GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").longValue;
    }
}
