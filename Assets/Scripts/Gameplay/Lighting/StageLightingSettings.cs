using UnityEngine;

namespace GhostHunter.Gameplay.Lighting
{
    /// <summary>
    /// 스테이지 집 조명(방 천장등·해·환경광·안개·반사)의 밝기 값. 방별 기준 반경·가중치는 씬의
    /// <see cref="StageRoomLight"/> 가 들고, 여기서는 전체 배율만 정한다.
    /// 튜닝 창(F2)·접속 HUD(Tab) "조명" 섹션이 이 SO 를 직접 고친다(<c>TuningHud</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Lighting/Stage Lighting Settings", fileName = "StageLightingSettings")]
    public sealed class StageLightingSettings : ScriptableObject
    {
        [Header("방 천장등")]
        [Tooltip("꺼 두면 층·방 스위치와 무관하게 천장등이 전부 꺼진다. 불 꺼진 집에서 시작하는 것은 방 스위치 기본값(꺼짐)이 정한다.")]
        [SerializeField] private bool _roomLightsOn = true;
        [Tooltip("천장등 기본 밝기. 방마다 가중치(StageRoomLight)를 곱한다.")]
        [SerializeField, Range(0f, 12f)] private float _roomIntensity = 4f;
        [Tooltip("방마다 정해진 기준 반경에 곱하는 배율.")]
        [SerializeField, Range(0.3f, 2f)] private float _roomRangeScale = 1f;
        [Tooltip("색온도(K). 낮을수록 주황, 높을수록 푸르다.")]
        [SerializeField, Range(1500f, 12000f)] private float _roomColorTemperature = 3200f;
        [Tooltip("그림자를 끄면 빛이 벽을 뚫고 옆방까지 번진다. 대신 가볍다.")]
        [SerializeField] private LightShadows _roomShadows = LightShadows.Hard;
        [SerializeField, Range(0f, 1f)] private float _roomShadowStrength = 0.9f;
        [Tooltip("천장등 패널의 발광 세기. 조명이 꺼지면 패널도 꺼진다.")]
        [SerializeField, Range(0f, 8f)] private float _fixtureEmission = 2.5f;

        [Header("해 (Directional)")]
        [SerializeField] private bool _sunOn = true;
        [SerializeField, Range(0f, 3f)] private float _sunIntensity = 1f;
        [Tooltip("해 그림자가 꺼져 있으면 지붕을 뚫고 실내가 밝아진다.")]
        [SerializeField] private LightShadows _sunShadows = LightShadows.Soft;

        [Header("환경광")]
        [Tooltip("씬에 저장된 환경광(Trilight) 색에 곱한다. 실내 어둠의 바닥값이다.")]
        [SerializeField, Range(0f, AmbientScaleMax)] private float _ambientScale = 1f;

        public const float AmbientScaleMax = 6f;

        [Header("어둠")]
        [Tooltip("검은 안개. 거리에 따라 시야가 먹혀 들어간다 — 헤드라이트 불빛도 멀리선 사라진다.")]
        [SerializeField] private bool _fogOn = true;
        [Tooltip("Exponential Squared 안개 밀도. 0.025 이면 16m 에서 85%, 30m 에서 57% 만 보인다.")]
        [SerializeField, Range(0f, FogDensityMax)] private float _fogDensity = 0.025f;

        public const float FogDensityMax = 0.3f;
        [SerializeField] private Color _fogColor = Color.black;
        [Tooltip("하늘(스카이박스) 반사 세기. 켜 두면 불이 꺼져도 매끈한 면이 은은하게 빛나 어둠이 뜬다.")]
        [SerializeField, Range(0f, 1f)] private float _reflectionIntensity = 0.25f;

        /// <summary>값이 바뀔 때마다 증가한다. 컨트롤러가 이것만 비교해 다시 적용한다.</summary>
        public int Version { get; private set; }

        public bool RoomLightsOn => _roomLightsOn;
        public float RoomIntensity => Mathf.Max(0f, _roomIntensity);
        public float RoomRangeScale => Mathf.Clamp(_roomRangeScale, 0.3f, 2f);
        public Color RoomColor => Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(_roomColorTemperature, 1500f, 12000f));
        public LightShadows RoomShadows => _roomShadows;
        public float RoomShadowStrength => Mathf.Clamp01(_roomShadowStrength);
        public float FixtureEmission => Mathf.Max(0f, _fixtureEmission);
        public bool SunOn => _sunOn;
        public float SunIntensity => Mathf.Max(0f, _sunIntensity);
        public LightShadows SunShadows => _sunShadows;
        public float AmbientScale => Mathf.Max(0f, _ambientScale);
        public bool FogOn => _fogOn;
        public float FogDensity => Mathf.Max(0f, _fogDensity);
        public Color FogColor => _fogColor;
        public float ReflectionIntensity => Mathf.Clamp01(_reflectionIntensity);

        /// <summary>접속 HUD 환경광 슬라이더. 튜닝 창처럼 SO 를 직접 고친다(에디터에선 에셋에 남는다).</summary>
        public void SetAmbientScale(float scale)
        {
            float next = Mathf.Clamp(scale, 0f, AmbientScaleMax);
            if (Mathf.Approximately(next, _ambientScale))
                return;

            _ambientScale = next;
            Version++;
        }

        /// <summary>접속 HUD 검은 안개 스위치.</summary>
        public void SetFogOn(bool on)
        {
            if (_fogOn == on)
                return;

            _fogOn = on;
            Version++;
        }

        /// <summary>접속 HUD 검은 안개 밀도 슬라이더.</summary>
        public void SetFogDensity(float density)
        {
            float next = Mathf.Clamp(density, 0f, FogDensityMax);
            if (Mathf.Approximately(next, _fogDensity))
                return;

            _fogDensity = next;
            Version++;
        }

        // 인스펙터와 튜닝 창(리플렉션 호출) 모두 값을 바꾼 뒤 이것을 부른다.
        private void OnValidate() => Version++;
    }
}
