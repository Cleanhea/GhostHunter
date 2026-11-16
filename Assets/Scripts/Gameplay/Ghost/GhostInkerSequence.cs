using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 현상 하나의 진행표(project/ghost-inker.md §4.3·§5.3). 서버만 돌리는 순수 상태 기계라 EditMode 로 검증한다.
    /// 눈 맞춤·접근 같은 판정은 컨트롤러가 하고 결과만 <see cref="Tick"/> 에 넘긴다.
    /// </summary>
    public sealed class GhostInkerSequence
    {
        private InkerTimings _timings;

        public InkerPhenomenon Kind { get; private set; }
        public InkerStep Step { get; private set; }
        public float StepElapsed { get; private set; }

        /// <summary>현상 1 — ③ 대기 중에 눈이 마주쳤는가(④ 고개 기울이기를 거쳤는가).</summary>
        public bool EyeContactMade { get; private set; }

        /// <summary>현상 2 — 2m 안으로 다가와 글씨를 즉시 완성했는가.</summary>
        public bool Rushed { get; private set; }

        public bool IsRunning => Kind != InkerPhenomenon.None && Step != InkerStep.None && Step != InkerStep.Done;

        /// <summary>
        /// 글씨가 얼마나 써졌는가(0~1). 현상 1은 ⑥ 긁는 소리 동안, 현상 2는 쓰는 동안 차오른다.
        /// 1 이 되기 전에는 닦기·위반 판정을 하지 않는다.
        /// </summary>
        public float WritingProgress
        {
            get
            {
                switch (Kind)
                {
                    case InkerPhenomenon.DoorPeek:
                        if (Step == InkerStep.DoorScratch)
                            return Mathf.Clamp01(StepElapsed / Mathf.Max(0.01f, _timings.DoorScratch));
                        return Step == InkerStep.Done ? 1f : 0f;
                    case InkerPhenomenon.WallWriting:
                        if (Step == InkerStep.WallWriting)
                            return Mathf.Clamp01(StepElapsed / Mathf.Max(0.01f, _timings.WallWrite));
                        return Step == InkerStep.Done ? 1f : 0f;
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>현상 1을 시작한다. <paramref name="closeDoorFirst"/> 면 열린 문을 먼저 닫는 대기부터(디버그 강제 실행).</summary>
        public void StartDoorPeek(InkerTimings timings, bool closeDoorFirst)
        {
            Begin(InkerPhenomenon.DoorPeek, timings);
            Step = closeDoorFirst ? InkerStep.DoorPrepare : InkerStep.DoorOpening;
        }

        public void StartWallWriting(InkerTimings timings)
        {
            Begin(InkerPhenomenon.WallWriting, timings);
            Step = InkerStep.WallWriting;
        }

        public void Cancel()
        {
            Kind = InkerPhenomenon.None;
            Step = InkerStep.None;
            StepElapsed = 0f;
            EyeContactMade = false;
            Rushed = false;
        }

        /// <summary>
        /// 시간을 진행한다. <paramref name="eyeContact"/>: 이번 틱에 누군가와 눈이 마주쳤다(현상 1 ③에서만 본다).
        /// <paramref name="playerClose"/>: 누군가 글씨 자리 2m 안에 있다(현상 2 쓰는 중에만 본다). 단계가 바뀌었으면 true.
        /// </summary>
        public bool Tick(float deltaTime, bool eyeContact, bool playerClose)
        {
            if (!IsRunning)
                return false;

            StepElapsed += Mathf.Max(0f, deltaTime);
            switch (Step)
            {
                case InkerStep.DoorPrepare:
                    return Advance(_timings.DoorPrepare, InkerStep.DoorOpening);
                case InkerStep.DoorOpening:
                    return Advance(_timings.DoorOpen, InkerStep.DoorLean);
                case InkerStep.DoorLean:
                    return Advance(_timings.DoorLean, InkerStep.DoorWatching);
                case InkerStep.DoorWatching:
                    if (eyeContact)
                    {
                        EyeContactMade = true;
                        Enter(InkerStep.DoorTilt);
                        return true;
                    }

                    // 5초 안에 눈이 안 마주치면 ④를 건너뛰고 ⑤로.
                    return Advance(_timings.DoorWatch, InkerStep.DoorSlam);
                case InkerStep.DoorTilt:
                    return Advance(_timings.DoorTilt, InkerStep.DoorSlam);
                case InkerStep.DoorSlam:
                    return Advance(_timings.DoorSlam, InkerStep.DoorScratch);
                case InkerStep.DoorScratch:
                    return Advance(_timings.DoorScratch, InkerStep.Done);
                case InkerStep.WallWriting:
                    if (playerClose)
                    {
                        // 작성 속도를 높여 즉시 완성한다(원문 §3-3-1). 다 쓰면 현상이 끝난다(2026-11-16 — 목 돌리기·응시 없음).
                        Rushed = true;
                        Enter(InkerStep.Done);
                        return true;
                    }

                    return Advance(_timings.WallWrite, InkerStep.Done);
                default:
                    return false;
            }
        }

        private void Begin(InkerPhenomenon kind, InkerTimings timings)
        {
            Cancel();
            _timings = timings;
            Kind = kind;
        }

        private bool Advance(float duration, InkerStep next)
        {
            if (StepElapsed < duration)
                return false;

            float leftover = StepElapsed - Mathf.Max(0f, duration);
            Step = next;
            StepElapsed = next == InkerStep.Done ? 0f : leftover;
            return true;
        }

        private void Enter(InkerStep next)
        {
            Step = next;
            StepElapsed = 0f;
        }
    }
}
