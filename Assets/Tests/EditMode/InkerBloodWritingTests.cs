using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 인커 피 글씨(사용자 요청 2026-11-15) — 획 글꼴·줄바꿈·피 메시(쓰기 진행·흘러내림)·새 명령(문 열지 마·가구 만지지 마)의 규칙,
    /// 현상 단계별 화면 분위기 세기. 실제 화면 모양은 Unity 플레이로 확인한다.
    /// </summary>
    public sealed class InkerBloodWritingTests
    {
        private const float LetterHeight = 0.32f;
        private const float MaxLineWidth = 2.4f;

        private static IEnumerable<InkerCommand> AllCommands()
        {
            foreach (InkerCommand command in Enum.GetValues(typeof(InkerCommand)))
                yield return command;
        }

        [Test]
        public void 모든_명령_문구는_획_글꼴로_쓸_수_있다()
        {
            foreach (InkerCommand command in AllCommands())
            {
                string text = InkerCommandText.Of(command);
                foreach (char c in text)
                    Assert.IsTrue(InkerStrokeFont.Has(c), $"'{text}' 의 '{c}' 획이 없다");
                InkerTextLayout layout = InkerStrokeFont.Layout(text, 0f);
                Assert.Greater(layout.Strokes.Count, text.Replace(" ", string.Empty).Length - 1, text);
            }
        }

        [Test]
        public void 긴_명령은_낱말_단위로_줄을_바꿔_벽_폭_안에_쓴다()
        {
            float wrap = MaxLineWidth / LetterHeight;
            InkerTextLayout furniture = InkerStrokeFont.Layout(InkerCommandText.Of(InkerCommand.DontTouchFurniture), wrap);
            Assert.GreaterOrEqual(furniture.LineCount, 2, "가구 명령은 한 줄에 다 안 들어간다");
            Assert.LessOrEqual(furniture.Width, wrap + 1e-3f);

            InkerTextLayout jump = InkerStrokeFont.Layout(InkerCommandText.Of(InkerCommand.DontJump), wrap);
            Assert.AreEqual(1, jump.LineCount, "짧은 명령은 한 줄");

            foreach (InkerCommand command in AllCommands())
            {
                InkerStrokeFont.Size(InkerCommandText.Of(command), LetterHeight, MaxLineWidth,
                    out float width, out float height, out float wrapWidth, out float letter);
                Assert.AreEqual(LetterHeight, letter, 1e-5f, "벽 폭 안에 들어가면 글자 크기는 그대로");
                Assert.LessOrEqual(width, MaxLineWidth + InkerStrokeFont.RectPadding * 2f * LetterHeight + 1e-3f, command.ToString());
                Assert.Greater(height, LetterHeight, command.ToString());
                Assert.AreEqual(wrap, wrapWidth, 1e-4f);
            }
        }

        [Test]
        public void 좁은_문에는_줄을_바꾸고_그래도_넓으면_글자를_줄인다()
        {
            const float doorWidth = 0.6f;
            InkerStrokeFont.Size(InkerCommandText.SeeYou, LetterHeight, doorWidth,
                out float width, out _, out float wrap, out float letter);
            InkerTextLayout layout = InkerStrokeFont.Layout(InkerCommandText.SeeYou, wrap);
            Assert.GreaterOrEqual(layout.LineCount, 2, "I SEE / YOU");
            Assert.LessOrEqual(layout.Width * letter, doorWidth + 1e-4f, "글자 부분은 문짝 폭 안");
            Assert.LessOrEqual(letter, LetterHeight);
            Assert.Greater(width, 0f);
        }

        [Test]
        public void 피_글씨는_진행도만큼_그려지고_같은_씨앗이면_모양이_같다()
        {
            var a = new InkerBloodMesh("DON'T JUMP", 0f, 1234);
            var b = new InkerBloodMesh("DON'T JUMP", 0f, 1234);
            var c = new InkerBloodMesh("DON'T JUMP", 0f, 99);

            Assert.AreEqual(0, Count(a, 0f, 0f), "쓰기 전에는 아무것도 없다");
            int half = Count(a, 0.5f, 2f);
            int full = Count(a, 1f, 4f);
            Assert.Greater(half, 0);
            Assert.Greater(full, half, "다 쓰면 더 많이 그려진다");

            List<Vector3> va = Vertices(a, 1f, 30f);
            List<Vector3> vb = Vertices(b, 1f, 30f);
            List<Vector3> vc = Vertices(c, 1f, 30f);
            Assert.AreEqual(va.Count, vb.Count);
            for (int i = 0; i < va.Count; i += 37)
                Assert.That(Vector3.Distance(va[i], vb[i]), Is.LessThan(1e-5f), "같은 글씨 Id → 모든 피어가 같은 모양");
            Assert.IsTrue(va.Count != vc.Count || Vector3.Distance(va[5], vc[5]) > 1e-4f, "씨앗이 다르면 손글씨가 다르다");
        }

        [Test]
        public void 다_쓴_뒤에도_피가_흘러내리다가_멈춘다()
        {
            var blood = new InkerBloodMesh("DON'T TURN AROUND", 0f, 7);
            Assert.Greater(blood.DripCount, 0, "흘러내리는 자국이 있다");

            // 늦게 쓴 글자의 피는 다 쓴 직후 막 흐르기 시작한다 — 시간이 지나면 흘러내림이 길어져 정점이 는다.
            Assert.Greater(Count(blood, 1f, 60f), Count(blood, 1f, 4.05f), "시간이 지나면 피가 더 흘러내린다");
            Assert.LessOrEqual(LowestY(blood, 1f, 60f), LowestY(blood, 1f, 4.05f) + 1e-5f, "흘러내림은 위로 줄지 않는다");
            Assert.IsFalse(blood.IsSettled(1f, 4.05f, 4f), "막 다 쓴 직후에는 아직 흐른다");
            Assert.IsTrue(blood.IsSettled(1f, 120f, 4f), "충분히 지나면 멈춘다(메시를 더 짓지 않는다)");
            Assert.IsFalse(blood.IsSettled(0.5f, 120f, 4f), "쓰는 중이면 멈추지 않는다");
        }

        [Test]
        public void 손끝은_읽는_사람_기준_왼쪽에서_오른쪽으로_나아간다()
        {
            var blood = new InkerBloodMesh("DON'T JUMP", 0f, 3);
            Assert.IsTrue(blood.TryGetTip(0.02f, out Vector2 start));
            Assert.IsTrue(blood.TryGetTip(0.98f, out Vector2 end));
            Assert.Less(start.x, 0f, "첫 글자는 왼쪽");
            Assert.Greater(end.x, 0f, "마지막 글자는 오른쪽");
        }

        [Test]
        public void 글씨_뷰는_벽_면_앞에서_왼쪽부터_쓰고_다_쓰면_전부_그린다()
        {
            // 글씨 면이 -Z 를 본다 → 읽는 사람은 -Z 쪽에서 +Z 를 보고, 그 사람의 오른쪽은 +X 다.
            InkerStrokeFont.Size("DON'T JUMP", LetterHeight, MaxLineWidth,
                out float width, out float height, out float wrap, out float letter);
            var state = new InkerWritingState
            {
                Id = 1,
                Command = InkerCommand.DontJump,
                LocalPosition = new Vector3(0f, 1.5f, 0f),
                LocalRotation = Quaternion.LookRotation(Vector3.back),
                Width = width,
                Height = height,
                LetterHeight = letter,
                WrapWidth = wrap,
                RevealStartTime = 0d,
                RevealSeconds = 4f,
                WipeRemaining = 6,
                WipeRequired = 6,
            };
            InkerWritingView view = InkerWritingView.Create(state);
            try
            {
                view.Refresh(null, 0.2d);
                Assert.IsTrue(view.TryGetTipWorld(out Vector3 early));
                Assert.Less(early.x, 0f, "쓰기 시작은 읽는 사람의 왼쪽");
                Assert.AreEqual(0f, early.z, 1e-3f, "글씨는 면 위에 있다");
                int partial = view.VertexCount;

                view.Refresh(null, 3.9d);
                Assert.IsTrue(view.TryGetTipWorld(out Vector3 late));
                Assert.Greater(late.x, early.x + 0.5f, "손끝이 오른쪽으로 나아간다");

                state.Complete = true;
                view.Apply(state);
                view.Refresh(null, 3.9d);
                Assert.Greater(view.VertexCount, partial, "다 쓰면 전부 그린다");
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }
        }

        [Test]
        public void 명령마다_금지_행동이_따로_있고_I_SEE_YOU는_어길_수_없다()
        {
            Assert.IsTrue(InkerCommandRules.Breaks(InkerCommand.DontTurnAround, InkerActions.TurnedAround));
            Assert.IsTrue(InkerCommandRules.Breaks(InkerCommand.DontJump, InkerActions.Jumped));
            Assert.IsTrue(InkerCommandRules.Breaks(InkerCommand.DontOpenDoor, InkerActions.OpenedDoor));
            Assert.IsTrue(InkerCommandRules.Breaks(InkerCommand.DontTouchFurniture, InkerActions.TouchedFurniture));
            Assert.IsTrue(InkerCommandRules.Breaks(InkerCommand.DontOpenDoor, InkerActions.Jumped | InkerActions.OpenedDoor));

            Assert.IsFalse(InkerCommandRules.Breaks(InkerCommand.DontOpenDoor, InkerActions.TouchedFurniture | InkerActions.Jumped));
            Assert.IsFalse(InkerCommandRules.Breaks(InkerCommand.DontTouchFurniture, InkerActions.OpenedDoor));
            Assert.IsFalse(InkerCommandRules.Breaks(InkerCommand.None,
                InkerActions.TurnedAround | InkerActions.Jumped | InkerActions.OpenedDoor | InkerActions.TouchedFurniture));
        }

        [Test]
        public void 벽_글씨_명령은_켜진_네_가지_중에서_고르게_고른다()
        {
            var settings = ScriptableObject.CreateInstance<GhostInkerSettings>();
            try
            {
                Assert.AreEqual(4, settings.WallCommandCount);
                Assert.AreEqual(InkerCommand.DontTurnAround, settings.PickWallCommand(0.1f));
                Assert.AreEqual(InkerCommand.DontJump, settings.PickWallCommand(0.3f));
                Assert.AreEqual(InkerCommand.DontOpenDoor, settings.PickWallCommand(0.6f));
                Assert.AreEqual(InkerCommand.DontTouchFurniture, settings.PickWallCommand(0.9f));
                Assert.AreEqual(InkerCommand.DontTouchFurniture, settings.PickWallCommand(1f));

                Set(settings, "_commandDontTurnAround", false);
                Set(settings, "_commandDontJump", false);
                Assert.AreEqual(2, settings.WallCommandCount);
                Assert.AreEqual(InkerCommand.DontOpenDoor, settings.PickWallCommand(0.2f));
                Assert.AreEqual(InkerCommand.DontTouchFurniture, settings.PickWallCommand(0.7f));

                Set(settings, "_commandDontOpenDoor", false);
                Set(settings, "_commandDontTouchFurniture", false);
                Assert.AreEqual(InkerCommand.DontJump, settings.PickWallCommand(0.5f), "모두 꺼지면 점프 금지");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 화면_분위기는_눈이_마주치는_순간이_가장_진하고_현상이_없으면_꺼진다()
        {
            InkerTimings timings = InkerTimings.Original;
            Assert.AreEqual(0f, GhostPrototypeController.InkerStepMood(InkerStep.None, 0f, timings));
            Assert.AreEqual(0f, GhostPrototypeController.InkerStepMood(InkerStep.Done, 0f, timings));
            Assert.AreEqual(1f, GhostPrototypeController.InkerStepMood(InkerStep.DoorTilt, 0.1f, timings));

            float opening = GhostPrototypeController.InkerStepMood(InkerStep.DoorOpening, 0f, timings);
            float opened = GhostPrototypeController.InkerStepMood(InkerStep.DoorOpening, timings.DoorOpen, timings);
            Assert.Greater(opened, opening, "문이 열릴수록 조여 온다");
            float writing = GhostPrototypeController.InkerStepMood(InkerStep.WallWriting, 0f, timings);
            float written = GhostPrototypeController.InkerStepMood(InkerStep.WallWriting, timings.WallWrite, timings);
            Assert.Greater(written, writing, "벽 글씨는 써질수록 진해진다");
        }

        // ───────── 도우미 ─────────

        private static int Count(InkerBloodMesh blood, float progress, float seconds)
            => Vertices(blood, progress, seconds).Count;

        private static List<Vector3> Vertices(InkerBloodMesh blood, float progress, float seconds)
        {
            var vertices = new List<Vector3>();
            blood.Build(progress, seconds, 4f, LetterHeight, vertices, new List<Color32>(), new List<Vector2>(),
                new List<Vector2>(), new List<int>());
            return vertices;
        }

        private static float LowestY(InkerBloodMesh blood, float progress, float seconds)
        {
            float lowest = float.MaxValue;
            foreach (Vector3 vertex in Vertices(blood, progress, seconds))
                lowest = Mathf.Min(lowest, vertex.y);
            return lowest;
        }

        private static void Set(object target, string name, object value)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var field = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null)
                    continue;
                field.SetValue(target, value);
                return;
            }

            Assert.Fail($"{target.GetType().Name}.{name} 없음");
        }
    }
}
