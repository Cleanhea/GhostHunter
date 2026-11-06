using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 가구 운반 소음(원문 0.4 §7.3 "작은 물건 놓기"·"대형 가구 이동")을 주기적으로 훑어 찾는다.
    /// 충돌·낙하는 <c>FurnitureNetworkPhysics.ServerImpactReported</c>가 따로 알려 주므로 여기서 다루지 않는다.
    /// 가구 코드에는 손대지 않고 공개 <see cref="FurnitureGrabTarget.State"/>와 위치 변화만 읽는다.
    /// </summary>
    internal sealed class GhostFurnitureNoiseScanner
    {
        private readonly struct Entry
        {
            public readonly FurnitureState State;
            public readonly Vector3 Position;

            public Entry(FurnitureState state, Vector3 position)
            {
                State = state;
                Position = position;
            }
        }

        private readonly Dictionary<FurnitureGrabTarget, Entry> _entries = new();

        public void Clear() => _entries.Clear();

        /// <summary>
        /// 이전 훑기와 비교해 소음을 <paramref name="report"/>로 낸다.
        /// 들고 있는(잡기·던지기 준비) 큰 가구가 <paramref name="moveThreshold"/> 이상 움직였거나, 든 가구를 내려놓았을 때.
        /// 작은 물건은 내려놓을 때만 난다(들고 걷는 소리는 플레이어 발소리가 이미 낸다).
        /// </summary>
        public void Scan(FurnitureGrabTarget[] furniture, float moveThreshold,
            Func<FurnitureGrabTarget, bool> isSmall, Action<Vector3, GhostNoiseLevel> report)
        {
            if (furniture == null)
                return;

            float thresholdSqr = moveThreshold * moveThreshold;
            for (int i = 0; i < furniture.Length; i++)
            {
                FurnitureGrabTarget item = furniture[i];
                if (item == null || !item.IsSpawned)
                    continue;

                FurnitureState state = item.State;
                Vector3 position = item.transform.position;
                if (_entries.TryGetValue(item, out Entry previous))
                {
                    bool wasHeld = IsHeld(previous.State);
                    bool nowHeld = IsHeld(state);
                    if (nowHeld)
                    {
                        Vector3 delta = position - previous.Position;
                        delta.y = 0f;
                        if (delta.sqrMagnitude >= thresholdSqr && !isSmall(item))
                            report(position, GhostNoiseLevel.High);
                    }
                    else if (wasHeld && state == FurnitureState.Idle)
                    {
                        report(position, isSmall(item) ? GhostNoiseLevel.Medium : GhostNoiseLevel.High);
                    }
                }

                _entries[item] = new Entry(state, position);
            }
        }

        private static bool IsHeld(FurnitureState state)
            => state is FurnitureState.Held or FurnitureState.ThrowReady;
    }
}
