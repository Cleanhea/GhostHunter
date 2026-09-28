using System.Collections.Generic;

namespace GhostHunter.Gameplay.Lighting
{
    /// <summary>
    /// 접속 HUD(Tab) "조명" 섹션이 층·방 천장등을 켜고 끄는 창구. 밝기 값은
    /// <see cref="StageLightingSettings"/> 를 튜닝 창 렌더러로 직접 고친다.
    /// 로컬 전용이다 — 다른 접속자의 화면은 바뀌지 않는다.
    /// </summary>
    public interface IStageLightingDebug
    {
        /// <summary>씬에 배선된 천장등. HUD 가 층별로 묶어 그린다.</summary>
        IReadOnlyList<StageRoomLight> Lights { get; }

        int FloorCount { get; }

        string FloorName(int floor);

        /// <summary>그 층에 켜진 스위치가 하나라도 있는지.</summary>
        bool IsFloorOn(int floor);

        void SetFloorOn(int floor, bool on);

        void SetLightOn(int index, bool on);

        void SetAllOn(bool on);

        /// <summary>씬 환경광(Trilight)에 곱하는 배율. 0 ~ <see cref="StageLightingSettings.AmbientScaleMax"/>.</summary>
        float AmbientScale { get; set; }

        /// <summary>검은 안개(Exponential Squared) 스위치.</summary>
        bool FogOn { get; set; }

        /// <summary>검은 안개 밀도. 0 ~ <see cref="StageLightingSettings.FogDensityMax"/>.</summary>
        float FogDensity { get; set; }

        /// <summary>켜진 개수·설정 요약 한 줄.</summary>
        string StatusSummary { get; }
    }
}
