using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 집 루트 아래 방마다 하나씩 있는 <c>RoomDimensions</c> 상자(방 바닥 크기)로 "방 안"을 판정한다.
    /// 부활 의식 방 고르기와 분해 가능 목표 가구 후보 고르기가 같은 기준을 쓴다.
    /// </summary>
    public static class HouseRoomVolumes
    {
        public const string VolumeName = "RoomDimensions";

        /// <summary>방 상자를 계층 순서로 모은다 — 모든 피어가 같은 씬이라 같은 순서다.</summary>
        public static void Collect(Transform houseRoot, List<BoxCollider> volumes)
        {
            volumes.Clear();
            if (houseRoot == null)
                return;
            foreach (BoxCollider box in houseRoot.GetComponentsInChildren<BoxCollider>(true))
                if (box.name == VolumeName && box.transform.parent != null)
                    volumes.Add(box);
        }

        /// <summary>바닥 넓이 안이고, 바닥 1m 아래부터 2.5m 위까지인가(바닥에 놓인 가구의 원점이 들어간다).</summary>
        public static bool Contains(BoxCollider volume, Vector3 position)
        {
            Vector3 local = volume.transform.InverseTransformPoint(position) - volume.center;
            Vector3 half = volume.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z
                && local.y >= -1f && local.y <= 2.5f;
        }

        public static bool ContainsAny(List<BoxCollider> volumes, Vector3 position)
        {
            for (int i = 0; i < volumes.Count; i++)
                if (volumes[i] != null && Contains(volumes[i], position))
                    return true;
            return false;
        }
    }
}
