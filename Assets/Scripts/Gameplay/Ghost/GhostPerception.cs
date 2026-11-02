using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>시야·시야선·이동 소리 판정. 은신·안전구역 후보 정책은 Controller가 제공한다.</summary>
    internal sealed class GhostPerception
    {
        private readonly Transform _ghost;
        private readonly GhostPrototypeSettings _settings;
        private readonly System.Func<SanityNetworkState, bool> _canChasePlayer;
        private readonly Dictionary<ulong, Vector3> _previousPlayerPositions = new();
        private readonly Dictionary<ulong, float> _playerSpeeds = new();

        public GhostPerception(Transform ghost, GhostPrototypeSettings settings,
            System.Func<SanityNetworkState, bool> canChasePlayer)
        {
            _ghost = ghost;
            _settings = settings;
            _canChasePlayer = canChasePlayer;
        }

        public void Clear()
        {
            _previousPlayerPositions.Clear();
            _playerSpeeds.Clear();
        }

        public bool TryDetect(SanityNetworkState[] players, int count, bool highRisk,
            out SanityNetworkState detected)
        {
            detected = null;
            Vector3 eye = _ghost.position + Vector3.up * _settings.GhostEyeHeight;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                SanityNetworkState player = players[i];
                if (!_canChasePlayer(player))
                    continue;
                Vector3 center = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
                float distance = Vector3.Distance(eye, center);
                if (distance >= bestDistance)
                    continue;
                if (CanHear(player, distance) || HasVision(eye, center, player.transform, highRisk, includeNearby: true))
                {
                    detected = player;
                    bestDistance = distance;
                }
            }
            return detected != null;
        }

        public bool CanSense(SanityNetworkState player, bool highRisk)
        {
            Vector3 eye = _ghost.position + Vector3.up * _settings.GhostEyeHeight;
            Vector3 center = player.transform.position + Vector3.up * _settings.TargetCenterHeight;
            return CanHear(player, Vector3.Distance(eye, center))
                || HasVision(eye, center, player.transform, highRisk, includeNearby: true);
        }

        /// <summary>원뿔 시야·시야선만 확인한다. 침대 은신 판정에는 근거리·소리를 섞지 않는다.</summary>
        public bool CanSee(SanityNetworkState player, bool highRisk)
        {
            return HasVision(_ghost.position + Vector3.up * _settings.GhostEyeHeight,
                player.transform.position + Vector3.up * _settings.TargetCenterHeight,
                player.transform, highRisk, includeNearby: false);
        }

        public bool CanWitnessBody(SanityNetworkState player)
        {
            if (!player.HasSanity || player.IsBurrowed
                || HidingSpot.Contains(player.transform.position)
                || (player.IsProne && BedHideZone.Contains(player.transform.position)))
            {
                return false;
            }

            Vector3 eye = player.transform.position + Vector3.up * _settings.PhenomenonWitnessEyeHeight;
            Vector3 ghostCenter = _ghost.position + Vector3.up * _settings.GhostEyeHeight;
            Vector3 toGhost = ghostCenter - eye;
            float distance = toGhost.magnitude;
            return GhostVision.IsInsideCone(
                    player.transform.forward,
                    toGhost,
                    _settings.PhenomenonWitnessAngle,
                    distance,
                    _settings.PhenomenonWitnessDistance)
                && HasLineOfSight(eye, ghostCenter, _ghost);
        }

        public static bool HasLineOfSight(Vector3 from, Vector3 to, Transform player)
        {
            if (!Physics.Linecast(
                    from,
                    to,
                    out RaycastHit hit,
                    GameLayers.NonGhostPrototypeRaycastMask,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.transform == player || hit.transform.IsChildOf(player);
        }

        public void UpdateSpeeds(SanityNetworkState[] players, int playerCount, float deltaTime)
        {
            for (int i = 0; i < playerCount; i++)
            {
                SanityNetworkState player = players[i];
                if (player == null || !player.IsSpawned)
                    continue;

                ulong id = player.OwnerClientId;
                Vector3 position = player.transform.position;

                if (deltaTime > 0f && _previousPlayerPositions.TryGetValue(id, out Vector3 previous))
                {
                    Vector3 delta = position - previous;
                    delta.y = 0f;
                    float instantaneous = delta.magnitude / deltaTime;
                    float smoothed = _playerSpeeds.TryGetValue(id, out float last)
                        ? Mathf.Lerp(last, instantaneous, 0.5f)
                        : instantaneous;
                    _playerSpeeds[id] = smoothed;
                }

                _previousPlayerPositions[id] = position;
            }
        }
        private bool HasVision(Vector3 eye, Vector3 center, Transform target, bool highRisk, bool includeNearby)
        {
            Vector3 toTarget = center - eye;
            float distance = toTarget.magnitude;
            bool nearby = includeNearby && _settings.NearDetectRadius > 0f && distance <= _settings.NearDetectRadius;
            bool inCone = GhostVision.IsInsideCone(_ghost.forward, toTarget,
                highRisk ? _settings.HighRiskVisionAngle : _settings.VisionAngle, distance,
                highRisk ? _settings.HighRiskVisionDistance : _settings.VisionDistance);
            return (nearby || inCone) && HasLineOfSight(eye, center, target);
        }

        private bool CanHear(SanityNetworkState player, float distance)
        {
            // 기획서 §8.3: 웅크려 이동하면 속력과 무관하게 소리 탐지에서 제외한다.
            // 엎드려 기어서 이동하는 것도 소리를 내지 않는 것으로 친다(P1 확장, player-controller.md).
            if (player.IsCrouching
                || player.IsProne
                || !_playerSpeeds.TryGetValue(player.OwnerClientId, out float speed))
            {
                return false;
            }

            float radius = speed >= _settings.RunSpeedThreshold
                ? _settings.RunHearingRadius
                : speed >= _settings.WalkSpeedThreshold
                    ? _settings.WalkHearingRadius
                    : 0f;

            return radius > 0f && distance <= radius;
        }
    }
}
