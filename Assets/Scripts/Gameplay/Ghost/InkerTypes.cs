using System;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 명령 글씨가 금지하는 행동(project/ghost-inker.md §3). <see cref="None"/> 은 현상 1의 "I SEE YOU" 처럼
    /// 금지하는 행동이 없는 글씨다 — 닦기만 하면 되고 위반은 없다(IK-3 임시 해석).
    /// </summary>
    public enum InkerCommand : byte
    {
        None = 0,
        DontTurnAround = 1,
        DontJump = 2,

        /// <summary>문을 열지 마(사용자 요청 2026-11-15). 닫힌 문을 플레이어가 열면 위반.</summary>
        DontOpenDoor = 3,

        /// <summary>가구를 만지지 마(사용자 요청 2026-11-15). 가구를 잡기 시작하거나 분해하면 위반.</summary>
        DontTouchFurniture = 4,
    }

    /// <summary>서버가 한 틱 동안 본 플레이어 행동 — 명령 위반 판정 입력.</summary>
    [Flags]
    public enum InkerActions : byte
    {
        None = 0,
        TurnedAround = 1,
        Jumped = 2,
        OpenedDoor = 4,
        TouchedFurniture = 8,
    }

    /// <summary>명령 ↔ 금지 행동. 순수 계산이라 EditMode 로 검증한다.</summary>
    public static class InkerCommandRules
    {
        /// <summary>그 명령이 금지하는 행동.</summary>
        public static InkerActions Forbidden(InkerCommand command) => command switch
        {
            InkerCommand.DontTurnAround => InkerActions.TurnedAround,
            InkerCommand.DontJump => InkerActions.Jumped,
            InkerCommand.DontOpenDoor => InkerActions.OpenedDoor,
            InkerCommand.DontTouchFurniture => InkerActions.TouchedFurniture,
            _ => InkerActions.None,
        };

        /// <summary>이번 행동이 그 명령을 어겼는가. "I SEE YOU"(None)는 어길 수 없다.</summary>
        public static bool Breaks(InkerCommand command, InkerActions actions)
        {
            InkerActions forbidden = Forbidden(command);
            return forbidden != InkerActions.None && (actions & forbidden) != 0;
        }
    }

    /// <summary>인커 귀신이 글씨를 남기는 초자연현상 두 종류.</summary>
    public enum InkerPhenomenon : byte
    {
        None = 0,

        /// <summary>현상 1 — 문틈으로 지켜보는 귀신(문에 "I SEE YOU").</summary>
        DoorPeek = 1,

        /// <summary>현상 2 — 벽에 명령 글씨가 저절로 써진다(2026-11-16 사용자 요청: 귀신 모습 없이 허공이 쓰는 느낌).</summary>
        WallWriting = 2,
    }

    /// <summary>현상 진행 단계(원문 진행표의 행). 값은 네트워크로 복제되므로 바꾸지 않는다.</summary>
    public enum InkerStep : byte
    {
        None = 0,

        /// <summary>디버그 강제 실행에서 열린 문을 먼저 닫는 대기. 자연 발동에서는 닫힌 문만 고르므로 거치지 않는다.</summary>
        DoorPrepare = 1,

        /// <summary>① 문이 천천히 20° 열린다(2초).</summary>
        DoorOpening = 2,

        /// <summary>② 문틈으로 얼굴을 내민다(1초).</summary>
        DoorLean = 3,

        /// <summary>③ 플레이어를 바라보며 대기(최대 5초). 눈이 마주치면 ④로.</summary>
        DoorWatching = 4,

        /// <summary>④ 고개를 30° 기울인다(0.5초). 눈이 안 마주치면 건너뛴다.</summary>
        DoorTilt = 5,

        /// <summary>⑤ 문 뒤로 사라지고 문이 세게 닫힌다(0.5초).</summary>
        DoorSlam = 6,

        /// <summary>⑥ 문 바깥에서 긁는 소리, 글씨가 나타난다(2초).</summary>
        DoorScratch = 7,

        /// <summary>
        /// 벽에 글씨가 저절로 써진다(4초). 2m 안으로 다가오면 즉시 완성. 다 쓰면 현상이 끝난다 — 원문의 목 돌리기·응시·소멸
        /// (옛 값 11~13)은 2026-11-16 사용자 요청으로 없앴다. 빈 값은 다시 쓰지 않는다.
        /// </summary>
        WallWriting = 10,

        Done = 255,
    }

    public static class InkerCommandText
    {
        public const string SeeYou = "I SEE YOU";

        /// <summary>벽·문에 쓰는 문구. 원문 예시("Don't turn around", "DON'T JUMP.")를 대문자로 쓴다(IK-2 임시 해석).</summary>
        public static string Of(InkerCommand command) => command switch
        {
            InkerCommand.DontTurnAround => "DON'T TURN AROUND",
            InkerCommand.DontJump => "DON'T JUMP",
            InkerCommand.DontOpenDoor => "DON'T OPEN THE DOOR",
            InkerCommand.DontTouchFurniture => "DON'T TOUCH THE FURNITURE",
            _ => SeeYou,
        };

        /// <summary>Hub 표시용 한글 이름.</summary>
        public static string Label(InkerCommand command) => command switch
        {
            InkerCommand.DontTurnAround => "뒤돌지 마",
            InkerCommand.DontJump => "점프하지 마",
            InkerCommand.DontOpenDoor => "문 열지 마",
            InkerCommand.DontTouchFurniture => "가구 만지지 마",
            _ => "I SEE YOU(명령 없음)",
        };
    }

    /// <summary>
    /// 벽·문에 남은 명령 글씨 하나. 서버가 <c>NetworkList</c> 로 복제하고 각 피어가 그린다.
    /// 문에 쓴 글씨는 문(<see cref="AnchorId"/>)을 따라 돈다 — 위치·회전은 앵커 기준 로컬 값이다.
    /// </summary>
    public struct InkerWritingState : INetworkSerializable, IEquatable<InkerWritingState>
    {
        public int Id;
        public InkerCommand Command;
        public InkerPhenomenon Source;

        /// <summary>글씨가 붙은 문 NetworkObjectId. 0 이면 월드 좌표(벽).</summary>
        public ulong AnchorId;

        public Vector3 LocalPosition;

        /// <summary>글씨 면 회전 — forward 가 표면 바깥(읽는 사람 쪽)이다.</summary>
        public Quaternion LocalRotation;

        /// <summary>글씨 판정 사각형 폭·높이(m) — 여러 줄이면 모든 줄을 덮는다.</summary>
        public float Width;
        public float Height;

        /// <summary>글자(대문자) 높이(m)와 줄바꿈 폭(글자 높이 단위) — 피어가 서버와 같은 줄바꿈으로 피 글씨를 그린다.</summary>
        public float LetterHeight;
        public float WrapWidth;

        /// <summary>쓰기 시작한 서버 시각과 걸리는 시간 — 피어가 글자를 차례로 보여 주는 데만 쓴다.</summary>
        public double RevealStartTime;
        public float RevealSeconds;

        /// <summary>다 썼는가. 다 쓰기 전에는 닦기·위반·발견 판정을 하지 않는다(원문 §3-3-1).</summary>
        public bool Complete;

        public byte WipeRemaining;
        public byte WipeRequired;

        public string Text => InkerCommandText.Of(Command);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Command);
            serializer.SerializeValue(ref Source);
            serializer.SerializeValue(ref AnchorId);
            serializer.SerializeValue(ref LocalPosition);
            serializer.SerializeValue(ref LocalRotation);
            serializer.SerializeValue(ref Width);
            serializer.SerializeValue(ref Height);
            serializer.SerializeValue(ref LetterHeight);
            serializer.SerializeValue(ref WrapWidth);
            serializer.SerializeValue(ref RevealStartTime);
            serializer.SerializeValue(ref RevealSeconds);
            serializer.SerializeValue(ref Complete);
            serializer.SerializeValue(ref WipeRemaining);
            serializer.SerializeValue(ref WipeRequired);
        }

        public bool Equals(InkerWritingState other) => Id == other.Id && Command == other.Command
            && Source == other.Source && AnchorId == other.AnchorId
            && LocalPosition == other.LocalPosition && LocalRotation == other.LocalRotation
            && Width == other.Width && Height == other.Height
            && LetterHeight == other.LetterHeight && WrapWidth == other.WrapWidth
            && RevealStartTime == other.RevealStartTime && RevealSeconds == other.RevealSeconds
            && Complete == other.Complete && WipeRemaining == other.WipeRemaining
            && WipeRequired == other.WipeRequired;

        public override bool Equals(object obj) => obj is InkerWritingState other && Equals(other);

        public override int GetHashCode() => Id;
    }

    /// <summary>
    /// 진행 중인 현상 하나. 서버가 단계가 바뀔 때만 갱신하고, 피어는 단계 시작 서버 시각과 경과로 인형·문·소리를 재생한다.
    /// </summary>
    public struct InkerSequenceState : INetworkSerializable, IEquatable<InkerSequenceState>
    {
        public InkerPhenomenon Kind;
        public InkerStep Step;
        public double StepStartTime;

        /// <summary>현상 1: 인형이 서는 자리(문 뒤 숨은 자리). 현상 2: 글씨 앞 바닥 — 2m 접근·쓰는 소리·화면 연출의 기준점(인형 없음).</summary>
        public Vector3 PuppetPosition;

        /// <summary>몸이 보는 방향(도). 현상 1만 쓴다.</summary>
        public float BodyYaw;

        /// <summary>얼굴이 볼 방향(도) — 대상 플레이어 쪽. 현상 1만 쓴다.</summary>
        public float FaceYaw;

        /// <summary>현상 1: 숨은 자리 → 문틈으로 내민 자리 이동량(월드).</summary>
        public Vector3 LeanOffset;

        public ulong DoorId;
        public int WritingId;

        public bool IsRunning => Kind != InkerPhenomenon.None && Step != InkerStep.None && Step != InkerStep.Done;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref Step);
            serializer.SerializeValue(ref StepStartTime);
            serializer.SerializeValue(ref PuppetPosition);
            serializer.SerializeValue(ref BodyYaw);
            serializer.SerializeValue(ref FaceYaw);
            serializer.SerializeValue(ref LeanOffset);
            serializer.SerializeValue(ref DoorId);
            serializer.SerializeValue(ref WritingId);
        }

        public bool Equals(InkerSequenceState other) => Kind == other.Kind && Step == other.Step
            && StepStartTime == other.StepStartTime && PuppetPosition == other.PuppetPosition
            && BodyYaw == other.BodyYaw && FaceYaw == other.FaceYaw && LeanOffset == other.LeanOffset
            && DoorId == other.DoorId && WritingId == other.WritingId;

        public override bool Equals(object obj) => obj is InkerSequenceState other && Equals(other);

        public override int GetHashCode() => ((int)Kind << 8) | (int)Step;
    }

    /// <summary>현상 단계별 시간(초). 설정 에셋(<see cref="GhostInkerSettings"/>)이 채운다.</summary>
    public struct InkerTimings
    {
        public float DoorPrepare;
        public float DoorOpen;
        public float DoorLean;
        public float DoorWatch;
        public float DoorTilt;
        public float DoorSlam;
        public float DoorScratch;
        public float WallWrite;

        /// <summary>원문 진행표 값. 문을 미리 닫는 대기(강제 실행 전용)만 원문에 없다.</summary>
        public static InkerTimings Original => new()
        {
            DoorPrepare = 0.6f,
            DoorOpen = 2f,
            DoorLean = 1f,
            DoorWatch = 5f,
            DoorTilt = 0.5f,
            DoorSlam = 0.5f,
            DoorScratch = 2f,
            WallWrite = 4f,
        };
    }

    /// <summary>글씨 면 판정. 순수 계산이라 EditMode 로 검증한다.</summary>
    public static class InkerWritingGeometry
    {
        /// <summary>
        /// 광선이 글씨 사각형(중심·회전·폭·높이)의 <b>앞면</b>에 닿는가. 뒤(벽 속·문 반대편)에서 오는 광선은 닿지 않는다.
        /// </summary>
        public static bool RaycastRect(Vector3 origin, Vector3 direction, float maxDistance,
            Vector3 center, Quaternion rotation, float width, float height, out float distance)
        {
            distance = 0f;
            Vector3 normal = rotation * Vector3.forward;
            float denominator = Vector3.Dot(direction, normal);
            if (denominator > -1e-4f)
                return false;

            float t = Vector3.Dot(center - origin, normal) / denominator;
            if (t < 0f || t > maxDistance)
                return false;

            Vector3 local = Quaternion.Inverse(rotation) * (origin + direction * t - center);
            if (Mathf.Abs(local.x) > width * 0.5f || Mathf.Abs(local.y) > height * 0.5f)
                return false;

            distance = t;
            return true;
        }

        /// <summary>
        /// 글씨의 월드 위치·회전. 앵커(문)가 있으면 그 트랜스폼 기준으로 바꾼다. 앵커를 못 찾으면 false.
        /// </summary>
        public static bool TryGetWorldPose(in InkerWritingState writing, Transform anchor,
            out Vector3 position, out Quaternion rotation)
        {
            if (writing.AnchorId == 0)
            {
                position = writing.LocalPosition;
                rotation = writing.LocalRotation;
                return true;
            }

            if (anchor == null)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            position = anchor.TransformPoint(writing.LocalPosition);
            rotation = anchor.rotation * writing.LocalRotation;
            return true;
        }

        /// <summary>읽는 사람이 글씨 앞쪽에 있는가(표면 법선 쪽).</summary>
        public static bool IsInFront(Vector3 viewer, Vector3 center, Quaternion rotation, float margin = 0.05f)
            => Vector3.Dot(viewer - center, rotation * Vector3.forward) > margin;
    }
}
