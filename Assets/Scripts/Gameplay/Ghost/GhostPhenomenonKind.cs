namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 시스템 기획서 §4.4·§6.5 의 초자연현상 종류. 프로토타입은 8종을 전부 열거하되,
    /// 오디오 에셋이 없는 <see cref="WallKnock"/>·<see cref="Footsteps"/> 는
    /// <see cref="GhostPrototypeSettings.SoundPhenomenaEnabled"/> 로 선택 Pool에서 빠진다.
    /// G-6 해결(사용자 확정 2026-08-31): 종류를 가리지 않고, 발생한 현상을 실제로 "본" 플레이어면
    /// 전부 `귀신 이벤트 목격`으로 쳐서 정신력을 깎는다 — <see cref="GhostPrototypeController"/>.
    /// ServerCheckPhenomenonWitnessed 참고.
    /// </summary>
    public enum GhostPhenomenonKind : byte
    {
        /// <summary>아무 현상도 발생하지 않음.</summary>
        None = 0,

        /// <summary>주변 물건 흔들기(§6.5 #1). 서버가 근처 가구에 진동 토크를 준다.</summary>
        ObjectShake = 1,

        /// <summary>작은 물건 떨어뜨리기(§6.5 #2). 서버가 Light 급 소품을 툭 밀어 떨어뜨린다.</summary>
        SmallObjectDrop = 2,

        /// <summary>문 열고 닫기(§6.5 #4). 근처 <see cref="Interaction.DoorInteractable"/> 를 토글한다.</summary>
        DoorMove = 3,

        /// <summary>서랍 열기(§6.5 #4). 근처 <see cref="Interaction.GhostDrawer"/> 를 잠깐 열었다 닫는다.</summary>
        DrawerOpen = 4,

        /// <summary>조명 깜빡임 또는 끄기(§6.5 #5). 근처 방 조명을 시드 기반으로 점멸시킨다.</summary>
        LightFlicker = 5,

        /// <summary>벽·문 두드리는 소리(§6.5 #7). 오디오 에셋 대기 — 기본값에서는 Pool에 없다.</summary>
        WallKnock = 6,

        /// <summary>플레이어 발소리(§6.5 #8). 오디오 에셋 대기 — 기본값에서는 Pool에 없다.</summary>
        Footsteps = 7,

        /// <summary>귀신 일시 출현(§4.4). 지정 위치에 반투명 형상을 잠깐 띄운다(§3.3).</summary>
        Apparition = 8,
    }
}
