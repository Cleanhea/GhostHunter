using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// '박치기 공룡' 귀신의 어택 중 반복 사이클(project/ghost-dino.md §2) 상태 기계. 순수 C# 이라 EditMode 로 검증한다.
    /// 추격(8초) → 돌진 준비(2초: 앞 1.3초 방향 추적, 뒤 0.7초 방향 고정) → 돌진(첫 충돌까지) → 스턴(5초) → 추격 … 을 반복한다.
    /// 서버만 돌린다. 이동·충돌·피해는 컨트롤러가 하고, 여기서는 지금이 어느 단계이고 방향이 고정됐는지만 계산한다.
    /// </summary>
    internal sealed class GhostDinoCharge
    {
        internal enum Stage : byte
        {
            Chase = 0,
            Prepare = 1,
            Charge = 2,
            Stun = 3,
        }

        /// <summary>추격 단계 길이(초). 이 시간이 지나고 타깃이 있으면 준비로 넘어간다.</summary>
        internal float ChaseSeconds { get; set; } = 8f;

        /// <summary>돌진 준비 길이(초).</summary>
        internal float PrepareSeconds { get; set; } = 2f;

        /// <summary>준비의 마지막 몇 초 동안 방향을 고정하는가. 그 앞은 타깃을 따라 방향을 조정한다.</summary>
        internal float DirectionLockSeconds { get; set; } = 0.7f;

        /// <summary>돌진이 끝난 뒤 스턴 길이(초).</summary>
        internal float StunSeconds { get; set; } = 5f;

        internal Stage Current { get; private set; }

        /// <summary>현재 단계에 들어온 뒤 흐른 시간(초).</summary>
        internal float Elapsed { get; private set; }

        /// <summary>
        /// 준비·돌진 중이다 — 이 동안에는 어택 시간이 끝나도 어택을 끝내지 않고 돌진까지 마친다(기획 예외).
        /// </summary>
        internal bool IsCommitted => Current is Stage.Prepare or Stage.Charge;

        /// <summary>준비의 마지막 구간(방향 고정)에 들어왔는가.</summary>
        internal bool IsDirectionLocked =>
            Current == Stage.Prepare && Elapsed >= PrepareSeconds - DirectionLockSeconds;

        /// <summary>어택에 들어갈 때(그리고 어택을 벗어날 때) 처음으로 되돌린다.</summary>
        internal void Reset()
        {
            Current = Stage.Chase;
            Elapsed = 0f;
        }

        /// <param name="hasTarget">
        /// 돌진 방향을 정할 타깃(마지막으로 타깃팅한 플레이어)이 있는가. 없으면 추격 시간이 지나도 준비로 넘어가지 않고 기다린다.
        /// </param>
        internal void Tick(float deltaTime, bool hasTarget)
        {
            Elapsed += Mathf.Max(0f, deltaTime);
            switch (Current)
            {
                case Stage.Chase:
                    if (hasTarget && Elapsed >= ChaseSeconds)
                        Enter(Stage.Prepare);
                    break;

                case Stage.Prepare:
                    if (Elapsed >= PrepareSeconds)
                        Enter(Stage.Charge);
                    break;

                case Stage.Stun:
                    if (Elapsed >= StunSeconds)
                        Enter(Stage.Chase);
                    break;
            }
        }

        /// <summary>돌진이 첫 충돌로 끝났다 — 스턴으로 들어간다. 돌진 중이 아니면 무시한다.</summary>
        internal void EndCharge()
        {
            if (Current == Stage.Charge)
                Enter(Stage.Stun);
        }

        /// <summary>돌진 거리(m)당 내구도 손실. ⌊거리 × 계수⌋, 단 1 미만이면 1(기획 §3).</summary>
        internal static int FurnitureDamage(float distance, float damagePerMeter)
        {
            return Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0f, distance) * Mathf.Max(0f, damagePerMeter)));
        }

        private void Enter(Stage stage)
        {
            Current = stage;
            Elapsed = 0f;
        }
    }
}
