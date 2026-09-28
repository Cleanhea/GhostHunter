using UnityEngine;

namespace GhostHunter.Gameplay.Lighting
{
    /// <summary>
    /// 스테이지 집 조명(방 천장등·해·환경광)의 밝기 값. 방별 기준 반경·가중치는 씬의
    /// <see cref="StageRoomLight"/> 가 들고, 여기서는 전체 배율만 정한다.
    /// 튜닝 창(F2)·접속 HUD(Tab) "조명" 섹션이 이 SO 를 직접 고친다(<c>TuningHud</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Lighting/Stage Lighting Settings", fileName = "StageLightingSettings")]
    public sealed class StageLightingSettings : ScriptableObject
    {
        [Header("방 천장등")]
        [Tooltip("꺼 두면 층·방 스위치와 무관하게 천장등이 전부 꺼진다.")]
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
        [SerializeField, Range(0f, 2f)] private float _ambientScale = 0.45f;

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

        // 인스펙터와 튜닝 창(리플렉션 호출) 모두 값을 바꾼 뒤 이것을 부른다.
        private void OnValidate() => Version++;
    }
}
