using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.FurnitureDriver;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 드릴카 반출 구역에서 목표 가구의 <b>포장</b>(반출 완료)을 서버가 확정한다. 랜덤 풀 목표 가구와
    /// 분해 가능 목표 가구(짐더미·온전한 모양 모두, MD-3)를 함께 본다.
    ///
    /// <para>2026-11-03 기획 "2-2. 가구 포장 처리": 구역 안에 가구의 85% 이상이 있는 상태가 1초 이상 이어지면 포장된다.
    /// 아무도 붙잡지 않고 바닥에 내려놓은 상태여야 한다(<see cref="FurnitureState.Idle"/> + 홀더 없음 + 거의 정지).
    /// 조건이 한 스텝이라도 어긋나면 처음부터 센다. 완료되면 가구는 맵에서 사라지고 정산에 집계되며, 드릴카 짐칸에는
    /// <see cref="DeliveredCargoPile"/> 이 상자(짐)를 하나 쌓는다. 비율·시간·속력은 <see cref="FurnitureSpawnSettings"/> 값이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FurnitureDeliveryTracker : MonoBehaviour
    {
        // 서버 FixedUpdate 안에서만 쓴다(재사용 버퍼 — 할당 없음).
        private static readonly List<Collider> ColliderBuffer = new();

        // 목표 가구별로 조건이 이어진 시간(초). 완료되거나 조건이 깨지면 지운다.
        private readonly Dictionary<Component, float> _elapsed = new();
        private readonly List<Component> _stale = new();
        private FurnitureSpawnController _furniture;

        private void FixedUpdate()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer
                || GhostHunter.Gameplay.Recovery.StageRecoveryGate.Restoring)
                return;

            if (_furniture == null)
                _furniture = FindFirstObjectByType<FurnitureSpawnController>();
            if (_furniture == null || !_furniture.IsReady || _furniture.Settings == null)
                return;

            Step(Time.fixedDeltaTime, _furniture.Items, _furniture.Settings);
        }

        /// <summary>한 물리 스텝의 포장 판정. 테스트가 시간을 직접 흘려 보내려고 분리했다.</summary>
        internal void Step(float deltaTime, RandomFurnitureItem[] items, FurnitureSpawnSettings settings)
        {
            for (int i = 0; i < items.Length; i++)
            {
                RandomFurnitureItem item = items[i];
                if (item != null && item.IsWorkTarget && AdvancePacking(item, deltaTime, settings))
                    item.ServerCompleteDelivery();
            }

            IReadOnlyList<FurnitureDriverPoolItem> pool = FurnitureDriverPoolItem.All;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                FurnitureDriverPoolItem item = pool[i];
                if (item != null && item.IsWorkTarget && AdvancePacking(item, deltaTime, settings))
                    item.ServerCompleteDelivery();
            }

            ForgetFinished();
        }

        /// <summary>이 가구의 조건이 이어진 시간을 갱신하고, 포장 시간에 닿았으면 true.</summary>
        private bool AdvancePacking(Component item, float deltaTime, FurnitureSpawnSettings settings)
        {
            _elapsed.TryGetValue(item, out float elapsed);
            elapsed = FurniturePackingRule.Advance(elapsed, IsPackReady(item, settings), deltaTime);
            if (elapsed <= 0f)
            {
                _elapsed.Remove(item);
                return false;
            }

            _elapsed[item] = elapsed;
            return FurniturePackingRule.IsComplete(elapsed, settings.PackHoldSeconds);
        }

        /// <summary>지금 이 순간 포장 조건을 모두 채웠는가 — 내려놓음·정지·구역 안 비율.</summary>
        private static bool IsPackReady(Component item, FurnitureSpawnSettings settings)
        {
            FurnitureDeliveryZone zone = FurnitureDeliveryZone.Current;
            // 완료 처리(ServerCompleteDelivery)도 가구 원점이 구역 안일 것을 요구한다 — 싼 검사를 먼저 한다.
            if (zone == null || !FurnitureDeliveryZone.Contains(item.transform.position))
                return false;

            // 아무도 붙잡지 않고 바닥에 내려놓은 상태: 잡힘·투척 준비·발사 중이 아니어야 한다.
            FurnitureGrabTarget grab = item.GetComponent<FurnitureGrabTarget>();
            if (grab != null && (grab.State != FurnitureState.Idle || grab.HolderCount > 0))
                return false;

            // 떨어지거나 밀려 가는 중이 아니어야 한다.
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body != null && body.linearVelocity.sqrMagnitude > settings.PackMaxSpeed * settings.PackMaxSpeed)
                return false;

            Bounds bounds = BodyBounds(item);
            return FurniturePackingRule.InsideFraction(bounds, zone.WorldToLocal, zone.Size) >= settings.PackInsideRatio;
        }

        /// <summary>가구 몸의 월드 경계 상자 — 켜져 있는 충돌체(트리거 제외)를 모두 합친다. 없으면 원점 한 점.</summary>
        private static Bounds BodyBounds(Component item)
        {
            item.GetComponentsInChildren(false, ColliderBuffer);
            bool any = false;
            var bounds = new Bounds(item.transform.position, Vector3.zero);
            for (int i = 0; i < ColliderBuffer.Count; i++)
            {
                Collider collider = ColliderBuffer[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (any)
                    bounds.Encapsulate(collider.bounds);
                else
                    bounds = collider.bounds;
                any = true;
            }

            ColliderBuffer.Clear();
            return bounds;
        }

        // 포장이 끝났거나(더 이상 목표가 아님) 사라진 가구의 기록을 치운다.
        private void ForgetFinished()
        {
            if (_elapsed.Count == 0)
                return;

            _stale.Clear();
            foreach (Component item in _elapsed.Keys)
            {
                if (item == null || !IsStillWorkTarget(item))
                    _stale.Add(item);
            }

            for (int i = 0; i < _stale.Count; i++)
                _elapsed.Remove(_stale[i]);
        }

        private static bool IsStillWorkTarget(Component item)
        {
            return item switch
            {
                RandomFurnitureItem random => random.IsWorkTarget,
                FurnitureDriverPoolItem pool => pool.IsWorkTarget,
                _ => false,
            };
        }
    }
}
