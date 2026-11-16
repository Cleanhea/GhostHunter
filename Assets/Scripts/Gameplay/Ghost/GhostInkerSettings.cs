using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// '명령(인커)' 귀신 설정(project/ghost-inker.md, 원문 0.1 — 메모 "말을 거는 귀신"). 기본 귀신 설정
    /// (<see cref="GhostPrototypeSettings"/>)을 그대로 상속하고 명령 글씨·현상 2종 값만 더한다 — 컨트롤러는 설정이 이 타입일
    /// 때만 인커 현상을 돌린다(ADR-0024). 원문에 없는 값은 툴팁에 [TEMP IK-n] 으로 표시했다.
    /// </summary>
    [CreateAssetMenu(
        menuName = "GhostHunter/Gameplay/Ghost Inker Settings",
        fileName = "GhostInkerSettings")]
    public sealed class GhostInkerSettings : GhostPrototypeSettings
    {
        [Header("Inker ghost · 명령 글씨 공통 (project/ghost-inker.md §3)")]
        [Tooltip("[TEMP IK-9] 어택이 아닐 때 이 주기(초)마다 현상별 확률을 굴린다. 원문은 '방에 들어가 있다면 n%'만 정했다.")]
        [SerializeField, Min(1f)] private float _inkerCheckInterval = 10f;
        [Tooltip("[TEMP IK-2] 현상 2가 쓸 수 있는 명령 — 'DON'T TURN AROUND'.")]
        [SerializeField] private bool _commandDontTurnAround = true;
        [Tooltip("[TEMP IK-2] 현상 2가 쓸 수 있는 명령 — 'DON'T JUMP'.")]
        [SerializeField] private bool _commandDontJump = true;
        [Tooltip("현상 2가 쓸 수 있는 명령 — 'DON'T OPEN THE DOOR'(사용자 요청 2026-11-15). 닫힌 문을 열면 위반.")]
        [SerializeField] private bool _commandDontOpenDoor = true;
        [Tooltip("현상 2가 쓸 수 있는 명령 — 'DON'T TOUCH THE FURNITURE'(사용자 요청 2026-11-15). 가구를 잡기 시작하거나 분해하면 위반.")]
        [SerializeField] private bool _commandDontTouchFurniture = true;
        [Tooltip("[TEMP IK-7] 글씨를 '전부 닦는' 데 필요한 대걸레 좌클릭 횟수.")]
        [SerializeField, Range(1, 30)] private int _writingWipeStrokes = 6;
        [Tooltip("[TEMP IK-5] 닦지 않고 명령을 어기면 깎는 개인 정신력. 원문 '10%' 를 0~100 척도의 10 으로 해석했다.")]
        [SerializeField, Range(0, 100)] private int _violationSanityDecrease = 10;
        [Tooltip("[TEMP IK-4·13] 글씨를 '발견'하는 최대 거리(m). 발견한 플레이어만 그 명령을 지켜야 한다.")]
        [SerializeField, Min(1f)] private float _discoverDistance = 8f;
        [Tooltip("[TEMP IK-4·13] 카메라 정면에서 글씨 중심까지 이 각도(도) 안이면 발견으로 본다.")]
        [SerializeField, Range(5f, 90f)] private float _discoverAngle = 35f;
        [Tooltip("[TEMP IK-2] 'DON'T TURN AROUND' — 이 시간(초) 안에 이 각도 이상 돌면 뒤돈 것으로 본다.")]
        [SerializeField, Min(0.1f)] private float _turnAroundWindowSeconds = 1f;
        [Tooltip("[TEMP IK-2] 'DON'T TURN AROUND' 판정 각도(도).")]
        [SerializeField, Range(60f, 180f)] private float _turnAroundDegrees = 135f;
        [Tooltip("[TEMP IK-6] 위반 시 귀신이 화내며(갑툭튀) 눈앞에 보이는 시간(초).")]
        [SerializeField, Min(0.1f)] private float _jumpScareSeconds = 0.8f;
        [Tooltip("[TEMP IK-6] 갑툭튀 귀신을 띄우는 눈앞 거리(m). 벽·가구가 더 가까우면 그 앞에 띄운다.")]
        [SerializeField, Range(0.3f, 1.5f)] private float _jumpScareDistance = 0.7f;
        [Tooltip("갑툭튀 귀신 몸의 발광 색(HDR, 몸 텍스처에 곱한다) — 조명과 무관하게 어두운 집에서도 보이게 한다(사용자 요청 2026-11-16). 검정이면 발광 없음.")]
        [SerializeField, ColorUsage(false, true)] private Color _jumpScareEmission = new(2.6f, 2.7f, 3f, 1f);
        [Tooltip("갑툭튀 귀신 얼굴 앞(턱 아래)에 다는 점광원 색.")]
        [SerializeField] private Color _jumpScareLightColor = new(0.85f, 0.92f, 1f, 1f);
        [Tooltip("갑툭튀 점광원 세기. 0 이면 달지 않는다.")]
        [SerializeField, Min(0f)] private float _jumpScareLightIntensity = 1.2f;
        [Tooltip("갑툭튀 점광원 범위(m).")]
        [SerializeField, Min(0.1f)] private float _jumpScareLightRange = 3f;
        [Tooltip("[TEMP] 글자(대문자) 높이(m). 폭은 문구 길이에 맞춘다.")]
        [SerializeField, Min(0.1f)] private float _writingHeight = 0.32f;
        [Tooltip("[TEMP] 벽 글씨 한 줄 최대 폭(m) — 넘으면 낱말 단위로 줄을 바꾼다(긴 명령은 두 줄).")]
        [SerializeField, Min(0.5f)] private float _writingMaxLineWidth = 2.4f;

        [Header("Inker 연출 · 피 글씨·화면 (사용자 요청 2026-11-15)")]
        [Tooltip("피 글씨 머티리얼(셰이더 GhostHunter/InkerBlood). 비면 실행 중 셰이더를 찾아 만든다.")]
        [SerializeField] private Material _writingMaterial;
        [Tooltip("현상 중 화면 연출(포스트프로세싱) — 붉은 비네트·채도 빠짐·필름 그레인, 쾅·목 꺾임·갑툭튀 때 충격.")]
        [SerializeField] private bool _atmosphereEnabled = true;
        [Tooltip("화면 연출 세기 배율(0~1.5).")]
        [SerializeField, Range(0f, 1.5f)] private float _atmosphereStrength = 1f;
        [Tooltip("현상 위치에서 이 거리(m) 안이면 화면 연출을 최대로 받는다.")]
        [SerializeField, Min(0f)] private float _atmosphereNearDistance = 6f;
        [Tooltip("현상 위치에서 이 거리(m) 밖이면 화면 연출을 받지 않는다.")]
        [SerializeField, Min(1f)] private float _atmosphereFarDistance = 16f;

        [Header("현상 1 · 문틈으로 지켜보는 귀신 (§4)")]
        [Tooltip("플레이어가 방(집 안)에 있을 때 판정마다 발생 확률. 원문 20%.")]
        [SerializeField, Range(0f, 1f)] private float _doorPeekChance = 0.2f;
        [Tooltip("[TEMP IK-10] 한 판(귀신 한 마리)에 최대 발생 횟수. 원문 '3번 이상 겪으면 재발동되지 않는다'.")]
        [SerializeField, Min(0)] private int _doorPeekMaxCount = 3;
        [Tooltip("플레이어와 문 사이 거리(m). 원문 2~6m.")]
        [SerializeField, Min(0f)] private float _doorPeekMinDistance = 2f;
        [SerializeField, Min(0.5f)] private float _doorPeekMaxDistance = 6f;
        [Tooltip("문 반경 이 거리(m) 안에 플레이어가 있으면 고르지 않는다. 원문 1m.")]
        [SerializeField, Min(0f)] private float _doorPeekClearRadius = 1f;
        [Tooltip("[TEMP] '카메라에서 문이 보인다' — 카메라 정면에서 문 중심까지 이 각도(도) 안 + 가림 없음.")]
        [SerializeField, Range(10f, 90f)] private float _doorVisibleAngle = 50f;
        [Tooltip("① 문이 열리는 각도(도). 원문 20°.")]
        [SerializeField, Range(5f, 60f)] private float _doorPeekOpenDegrees = 20f;
        [SerializeField, Min(0.1f)] private float _doorPeekOpenSeconds = 2f;
        [Tooltip("② 얼굴을 내미는 시간(초). 원문 1초.")]
        [SerializeField, Min(0.1f)] private float _doorPeekLeanSeconds = 1f;
        [Tooltip("③ 대기 최대 시간(초). 원문 5초.")]
        [SerializeField, Min(0.1f)] private float _doorPeekWatchSeconds = 5f;
        [Tooltip("④ 고개 기울이기 각도(도)·시간(초). 원문 30°·0.5초.")]
        [SerializeField, Range(0f, 90f)] private float _doorPeekTiltDegrees = 30f;
        [SerializeField, Min(0.05f)] private float _doorPeekTiltSeconds = 0.5f;
        [Tooltip("⑤ 숨으며 문이 닫히는 시간(초). 원문 0.5초.")]
        [SerializeField, Min(0.05f)] private float _doorPeekSlamSeconds = 0.5f;
        [Tooltip("⑥ 긁는 소리와 함께 글씨가 나타나는 시간(초). 원문 2초.")]
        [SerializeField, Min(0.1f)] private float _doorPeekScratchSeconds = 2f;
        [Tooltip("눈 맞춤: 카메라 정면 이 각도(도) 안에 귀신 얼굴. 원문 15°.")]
        [SerializeField, Range(1f, 60f)] private float _eyeContactAngle = 15f;
        [Tooltip("눈 맞춤: 그 상태가 이 시간(초) 이상. 원문 0.3초.")]
        [SerializeField, Min(0f)] private float _eyeContactSeconds = 0.3f;
        [Tooltip("눈이 마주치면 깎는 개인 정신력. 원문 −5.")]
        [SerializeField, Range(0, 100)] private int _eyeContactSanityDecrease = 5;
        [Tooltip("문 앞에서 'I SEE YOU' 글씨를 발견하면 깎는 개인 정신력. 원문 −3.")]
        [SerializeField, Range(0, 100)] private int _doorWritingSanityDecrease = 3;

        [Header("현상 2 · 벽에 저절로 써지는 글씨 (§5 — 2026-11-16 귀신 모습 없앰)")]
        [Tooltip("플레이어가 방(집 안)에 있을 때 판정마다 발생 확률. 원문 30%.")]
        [SerializeField, Range(0f, 1f)] private float _wallWritingChance = 0.3f;
        [Tooltip("[TEMP IK-10] 한 판(귀신 한 마리)에 최대 발생 횟수. 원문 '2번 이상 겪으면 재발동되지 않는다'.")]
        [SerializeField, Min(0)] private int _wallWritingMaxCount = 2;
        [Tooltip("플레이어와 글씨 생성 지점 사이 거리(m). 원문 2~7m.")]
        [SerializeField, Min(0f)] private float _wallWritingMinDistance = 2f;
        [SerializeField, Min(0.5f)] private float _wallWritingMaxDistance = 7f;
        [Tooltip("쓰는 시간(초). 원문 4초. 다 쓰면 현상이 끝난다 — 원문의 목 돌리기·응시·소멸은 없앴다(사용자 요청 2026-11-16).")]
        [SerializeField, Min(0.1f)] private float _wallWriteSeconds = 4f;
        [Tooltip("쓰는 중 플레이어가 글씨 자리(글씨 앞 바닥)에 이 거리(m) 안으로 오면 즉시 완성. 원문 2m(원문은 '귀신에게').")]
        [SerializeField, Min(0f)] private float _wallRushDistance = 2f;
        [Tooltip("[TEMP IK-17] 지정 지점 대신 벽을 찾는다 — 벽 앞 이 거리(m)에 빈 바닥이 있어야 쓰고, 그 자리를 접근·소리·화면 연출 기준점으로 쓴다.")]
        [SerializeField, Min(0.2f)] private float _wallStandOffset = 0.5f;
        [Tooltip("[TEMP] 글씨 중심 높이(m, 바닥 기준).")]
        [SerializeField, Min(0.3f)] private float _wallWritingHeight = 1.45f;

        public float InkerCheckInterval => _inkerCheckInterval;
        public int WritingWipeStrokes => Mathf.Clamp(_writingWipeStrokes, 1, 255);
        public int ViolationSanityDecrease => _violationSanityDecrease;
        public float DiscoverDistance => _discoverDistance;
        public float DiscoverAngle => _discoverAngle;
        public float TurnAroundWindowSeconds => _turnAroundWindowSeconds;
        public float TurnAroundDegrees => _turnAroundDegrees;
        public float JumpScareSeconds => _jumpScareSeconds;
        public float JumpScareDistance => _jumpScareDistance;
        public Color JumpScareEmission => _jumpScareEmission;
        public Color JumpScareLightColor => _jumpScareLightColor;
        public float JumpScareLightIntensity => _jumpScareLightIntensity;
        public float JumpScareLightRange => _jumpScareLightRange;
        public float WritingHeight => _writingHeight;
        public float WritingMaxLineWidth => _writingMaxLineWidth;
        public Material WritingMaterial => _writingMaterial;
        public bool AtmosphereEnabled => _atmosphereEnabled;
        public float AtmosphereStrength => _atmosphereStrength;
        public float AtmosphereNearDistance => _atmosphereNearDistance;
        public float AtmosphereFarDistance => Mathf.Max(_atmosphereFarDistance, _atmosphereNearDistance + 0.5f);

        public float DoorPeekChance => _doorPeekChance;
        public int DoorPeekMaxCount => _doorPeekMaxCount;
        public float DoorPeekMinDistance => _doorPeekMinDistance;
        public float DoorPeekMaxDistance => _doorPeekMaxDistance;
        public float DoorPeekClearRadius => _doorPeekClearRadius;
        public float DoorVisibleAngle => _doorVisibleAngle;
        public float DoorPeekOpenDegrees => _doorPeekOpenDegrees;
        public float DoorPeekTiltDegrees => _doorPeekTiltDegrees;
        public float EyeContactAngle => _eyeContactAngle;
        public float EyeContactSeconds => _eyeContactSeconds;
        public int EyeContactSanityDecrease => _eyeContactSanityDecrease;
        public int DoorWritingSanityDecrease => _doorWritingSanityDecrease;

        public float WallWritingChance => _wallWritingChance;
        public int WallWritingMaxCount => _wallWritingMaxCount;
        public float WallWritingMinDistance => _wallWritingMinDistance;
        public float WallWritingMaxDistance => _wallWritingMaxDistance;
        public float WallRushDistance => _wallRushDistance;
        public float WallStandOffset => _wallStandOffset;
        public float WallWritingHeight => _wallWritingHeight;

        public InkerTimings Timings => new()
        {
            DoorPrepare = InkerTimings.Original.DoorPrepare,
            DoorOpen = _doorPeekOpenSeconds,
            DoorLean = _doorPeekLeanSeconds,
            DoorWatch = _doorPeekWatchSeconds,
            DoorTilt = _doorPeekTiltSeconds,
            DoorSlam = _doorPeekSlamSeconds,
            DoorScratch = _doorPeekScratchSeconds,
            WallWrite = _wallWriteSeconds,
        };

        /// <summary>현상 2가 고를 수 있는 명령 수.</summary>
        public int WallCommandCount => (_commandDontTurnAround ? 1 : 0) + (_commandDontJump ? 1 : 0)
            + (_commandDontOpenDoor ? 1 : 0) + (_commandDontTouchFurniture ? 1 : 0);

        /// <summary>
        /// 현상 2가 쓸 명령을 켜진 명령 중에서 고르게 고른다(<paramref name="roll"/> 0~1). 모두 꺼져 있으면 점프 금지.
        /// </summary>
        public InkerCommand PickWallCommand(float roll)
        {
            int count = WallCommandCount;
            if (count == 0)
                return InkerCommand.DontJump;

            int pick = Mathf.Clamp((int)(Mathf.Clamp01(roll) * count), 0, count - 1);
            if (_commandDontTurnAround && pick-- == 0)
                return InkerCommand.DontTurnAround;
            if (_commandDontJump && pick-- == 0)
                return InkerCommand.DontJump;
            if (_commandDontOpenDoor && pick-- == 0)
                return InkerCommand.DontOpenDoor;
            return InkerCommand.DontTouchFurniture;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            _doorPeekMaxDistance = Mathf.Max(_doorPeekMaxDistance, _doorPeekMinDistance + 0.5f);
            _wallWritingMaxDistance = Mathf.Max(_wallWritingMaxDistance, _wallWritingMinDistance + 0.5f);
        }
    }
}
