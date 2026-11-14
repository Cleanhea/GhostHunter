namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>F1 HUD에서 귀신 프로토타입을 스폰하고 상태 전이를 강제해 보는 Host 전용 서비스.</summary>
    public interface IGhostDebug
    {
        /// <summary>Local Host(서버)에서 조작 가능한 상태인지.</summary>
        bool CanControl { get; }

        /// <summary>지금 귀신이 스폰되어 있는지.</summary>
        bool HasGhost { get; }

        /// <summary>상태 노출 정책(§3.3)과 무관하게 귀신 본체를 디버그로 강제 표시 중인지.</summary>
        bool IsGhostForcedVisible { get; }

        /// <summary>현재 상태·팀 정신력·어택 판정 카운트다운·청소 진행도 요약(§12.1 형식).</summary>
        string StatusSummary { get; }

        /// <summary>마지막 조작 결과 메시지.</summary>
        string LastStatus { get; }

        void SpawnGhost();

        /// <summary>Host 로컬 플레이어의 현재 위치에 귀신을 바로 스폰한다(디버그).</summary>
        void SpawnGhostAtPlayer();

        void DespawnGhost();

        /// <summary>특수 어택 Trigger — 즉시 경고 상태로 보낸다.</summary>
        void ForceSpecialAttack();

        /// <summary>강제 종료 — 진행 중인 어택을 끊고 30초 자연 진정에 들어간다.</summary>
        void ForceSuppression();

        /// <summary>지금 귀신이 기존 공통 초자연현상을 쓰는가(지금은 박치기만 [임시], 사용자 결정 2026-11-15).</summary>
        bool HasClassicPhenomena { get; }

        /// <summary>초자연현상 강제 — 주기를 무시하고 Pool에서 현상 하나를 랜덤으로 즉시 실행한다(§6).</summary>
        void ForcePhenomenon();

        /// <summary>지정한 초자연현상을 상태·주기와 무관하게 즉시 실행한다(기능별 패턴 시험용).</summary>
        void ForcePhenomenon(GhostPhenomenonKind kind);

        /// <summary>귀신 본체 렌더를 상태와 무관하게 강제 표시/해제한다(디버그, §3.3 정책은 그대로).</summary>
        void ToggleGhostVisible();

        /// <summary>정신력·청소와 무관하게 활동 조건을 세우는 토글.</summary>
        void ToggleForceActive();

        /// <summary>청소 진행도 디버그 스텁을 증감한다(0~100).</summary>
        void AddCleaningProgress(int delta);

        void ResetCleaningProgress();

        /// <summary>Hub 에서 고를 수 있는 귀신 종류 수(스포너의 종류 목록).</summary>
        int GhostTypeCount { get; }

        string GetGhostTypeLabel(int index);

        /// <summary>지금 스폰된 귀신의 종류. 없으면 -1.</summary>
        int ActiveGhostTypeIndex { get; }

        /// <summary>스폰·교체 버튼이 쓸 종류.</summary>
        int SelectedGhostTypeIndex { get; set; }

        /// <summary>기존 귀신을 지우고 고른 종류로 다시 스폰한다(스폰 지점 또는 내 위치).</summary>
        void ReplaceGhostWithSelected(bool atPlayer);

        /// <summary>지금 귀신이 인커(명령 글씨 귀신)인가.</summary>
        bool HasInkerGhost { get; }

        /// <summary>Hub 에서 인커 현상을 시험할 수 있는가 — 지금 귀신이 인커이거나 종류 목록에 인커가 있다.</summary>
        bool CanTestInker { get; }

        /// <summary>
        /// 인커 현상 하나를 확률·횟수·주기와 무관하게 바로 시작한다. 지금 귀신이 인커가 아니면 인커로 교체(스폰 지점)한 뒤 시작한다.
        /// </summary>
        void ForceInkerPhenomenon(InkerPhenomenon kind);

        /// <summary>인커 글씨를 모두 지운다.</summary>
        void ClearInkerWritings();
    }
}
