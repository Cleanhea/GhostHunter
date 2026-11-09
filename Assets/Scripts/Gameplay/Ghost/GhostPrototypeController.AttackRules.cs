using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Voice;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 공통 기획서 원문 0.3·0.4의 어택 규칙 중 컨트롤러가 직접 하는 것 — 어택 시작 때 플레이어 근처 스폰(7m),
    /// 쉬움 난이도의 사망 시 어택 종료, 말소리 소음 반응. 상태 기계 쪽 규칙(청소 40% 일회성 강제 어택·0~30 계속 어택)은
    /// <see cref="GhostStateMachine"/>에 있다. 수치는 모두 설정 에셋이고 원문에 없는 부분은 [임시 해석]이다 → ghost-system.md G-9·22·23·27·30.
    /// </summary>
    public sealed partial class GhostPrototypeController
    {
        private const int AttackSpawnCandidates = 16;
        private const float AttackSpawnSampleRadius = 2f;
        // NavMesh 경로 길이가 직선 반경의 이 배수를 넘으면 벽 너머라 '근처'가 아니다.
        private const float AttackSpawnPathSlack = 2f;

        // NavMeshPath 는 MonoBehaviour 생성 시점(필드 초기화)에 만들면 안 된다 — 처음 쓸 때 만든다.
        private NavMeshPath _attackSpawnPath;
        private int _attackLivingAtStart;

        /// <summary>어택이 시작될 때 한 번 — 생존자 수를 기억하고, 주변에 플레이어가 없으면 가까운 플레이어 근처로 스폰한다.</summary>
        private void ServerBeginAttackRules(int playerCount, int living)
        {
            _attackLivingAtStart = living;
            TrySpawnNearPlayer(playerCount);
        }

        /// <summary>
        /// 쉬움 난이도: 어택 중 생존자가 줄면(한 명이라도 죽으면) 어택을 자동 종료해 자연 진정으로 보낸다(원문 0.3).
        /// 보통·어려움은 팀원이 죽어도 어택이 계속된다.
        /// </summary>
        private void ServerTickDifficultyRules(int living)
        {
            if (_settings.Difficulty != GhostDifficulty.Easy || _machine == null || living >= _attackLivingAtStart)
                return;

            _attackLivingAtStart = living;
            if (_machine.ForceSuppression())
                Debug.Log("[GhostPrototype] 쉬움 난이도 — 플레이어가 죽어 어택을 종료합니다.", this);
        }

        /// <summary>
        /// 원문 0.4 §7.4 "주변에 플레이어가 없는데 어택이 발생할 경우 근처 7m 스폰되어서 찾는 이동 로직" [임시 해석 — G-30].
        /// 어택이 시작될 때 가장 가까운 추격 가능 플레이어가 스폰 반경 안에 없으면, 그 플레이어에게서 반경만큼 떨어진 곳 중
        /// 같은 층이고 걸어서 갈 수 있으며 가능하면 어떤 플레이어의 시야에도 걸리지 않는 NavMesh 지점으로 귀신을 옮긴다.
        /// </summary>
        private void TrySpawnNearPlayer(int playerCount)
        {
            if (!_settings.AttackSpawnNearPlayerEnabled)
                return;

            SanityNetworkState nearest = null;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (!CanChasePlayer(player))
                    continue;

                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = player;
                }
            }

            float radius = _settings.AttackSpawnRadius;
            if (nearest == null || nearestDistance <= radius)
                return;

            if (!TryFindAttackSpawnPoint(nearest, playerCount, radius, out Vector3 point))
                return;

            TeleportTo(point, nearest.transform.position);
            _navigation.InvalidatePath();
            _roamDestination = point;
            Debug.Log(
                $"[GhostPrototype] 어택 시작 — 주변에 플레이어가 없어 Client {nearest.OwnerClientId} 근처 " +
                $"{Vector3.Distance(point, nearest.transform.position):F1}m 로 스폰합니다.",
                this);
        }

        private bool TryFindAttackSpawnPoint(SanityNetworkState anchor, int playerCount, float radius, out Vector3 spawn)
        {
            spawn = default;
            Vector3 center = anchor.transform.position;
            if (!NavMesh.SamplePosition(center, out NavMeshHit anchorHit, AttackSpawnSampleRadius, NavMesh.AllAreas))
                return false;

            float startAngle = Random.value * 360f;
            bool found = false;
            for (int i = 0; i < AttackSpawnCandidates; i++)
            {
                float angle = (startAngle + i * (360f / AttackSpawnCandidates)) * Mathf.Deg2Rad;
                Vector3 candidate = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, AttackSpawnSampleRadius, NavMesh.AllAreas))
                    continue;

                Vector3 point = hit.position;
                if (Mathf.Abs(point.y - center.y) > GhostNavigation.ArrivalVerticalTolerance
                    || !IsInsideHouseBounds(point)
                    || DrillCarSafeZone.Contains(point)
                    || HidingSpot.Contains(point))
                {
                    continue;
                }

                _attackSpawnPath ??= new NavMeshPath();
                if (!NavMesh.CalculatePath(point, anchorHit.position, NavMesh.AllAreas, _attackSpawnPath)
                    || _attackSpawnPath.status != NavMeshPathStatus.PathComplete
                    || PathLength(_attackSpawnPath) > radius * AttackSpawnPathSlack)
                {
                    continue;
                }

                // 시야에 안 걸리는 첫 지점이면 바로 쓴다. 없으면 걸려도 쓸 수 있는 첫 지점.
                if (!IsSeenByAnyPlayer(point, playerCount))
                {
                    spawn = point;
                    return true;
                }

                if (!found)
                {
                    spawn = point;
                    found = true;
                }
            }

            return found;
        }

        private bool IsSeenByAnyPlayer(Vector3 point, int playerCount)
        {
            Vector3 eye = point + Vector3.up * _settings.GhostEyeHeight;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (!CanChasePlayer(player))
                    continue;

                Vector3 target = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
                if (GhostPerception.HasLineOfSight(eye, target, player.transform))
                    return true;
            }

            return false;
        }

        private static float PathLength(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>귀신을 순간이동시키고 바라볼 곳(플레이어)을 향하게 한다. 캐릭터 컨트롤러가 켜진 채로는 위치를 못 바꾼다.</summary>
        private void TeleportTo(Vector3 position, Vector3 lookAt)
        {
            Vector3 flat = lookAt - position;
            flat.y = 0f;
            Quaternion rotation = flat.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : transform.rotation;

            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
            if (TryGetComponent(out NetworkTransform networkTransform) && networkTransform.IsSpawned)
                networkTransform.Teleport(position, rotation, transform.localScale);
        }

        /// <summary>
        /// 원문 0.4 소음표 "음성 채팅 — 보통~높음, 말소리 크기에 따라 감지" [TEMP 수치 — G-27 ④].
        /// 말하는 플레이어(<see cref="PlayerVoiceEmitter.TryGetSpeechLevel"/>)의 위치를 소음 지점으로 보고하고,
        /// 입력 레벨이 클수록 귀신이 먼 곳에서도 듣는다. 어택 중 배회할 때만 반응한다(가구 소리와 같은 규칙).
        /// </summary>
        private void ReportVoiceNoise(int playerCount)
        {
            if (!_settings.VoiceNoiseEnabled || !_noiseScanAttacking || _pursuit != Pursuit.Roam)
                return;

            double now = Time.unscaledTimeAsDouble;
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = _players[i];
                if (player == null || !player.IsSpawned || !player.HasSanity
                    || !player.TryGetComponent(out PlayerVoiceEmitter voice)
                    || !voice.TryGetSpeechLevel(now, out float decibels))
                {
                    continue;
                }

                Vector3 position = player.transform.position;
                if (!IsInsideHouseBounds(position) || DrillCarSafeZone.Contains(position))
                    continue;

                float loudness = GhostVoiceNoise.Loudness(
                    decibels, _settings.VoiceNoiseQuietDb, _settings.VoiceNoiseLoudDb);
                float radius = Mathf.Lerp(
                    _settings.VoiceNoiseMinRadius,
                    Mathf.Max(_settings.VoiceNoiseMinRadius, _settings.VoiceNoiseMaxRadius),
                    loudness);
                if (CanHearNoiseAt(position, radius))
                    _noise.Report(position, GhostVoiceNoise.Level(loudness), Time.time, _settings.EvidenceMemorySeconds);
            }
        }
    }
}
