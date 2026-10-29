using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>짐 한 개의 자리. <see cref="Center"/> 는 짐 가운데의 구역 로컬 좌표다.</summary>
    internal readonly struct CargoPlacement
    {
        public readonly Vector3 Center;
        public readonly float YawDegrees;
        public readonly int Variant;

        public CargoPlacement(Vector3 center, float yawDegrees, int variant)
        {
            Center = center;
            YawDegrees = yawDegrees;
            Variant = variant;
        }
    }

    /// <summary>
    /// 화물칸 바닥을 격자 칸으로 나누고, 반출될 때마다 가장 낮게 쌓인 칸에 짐을 한 개씩 올린다.
    /// 난수는 시드로 고정하고 앞 짐의 자리는 뒤 짐에 영향받지 않아, 접속자마다 같은 더미를 보고 개수가 늘어도 이미 쌓인 짐이 움직이지 않는다.
    /// 구역 로컬 좌표는 x·z 가 구역 가운데, y 가 바닥(0)이다.
    /// </summary>
    internal sealed class DeliveredCargoLayout
    {
        private const float CellMargin = 0.98f;

        private readonly Vector3 _areaSize;
        private readonly Vector3[] _sizes;
        private readonly float _yawJitter;
        private readonly float _offsetJitter;
        private readonly int _columns;
        private readonly int _rows;
        private readonly float[] _stackHeights;
        private readonly System.Random _random;
        private int _placed;

        public DeliveredCargoLayout(Vector3 areaSize, Vector3[] crateSizes, int seed,
            float yawJitterDegrees, float offsetJitter)
        {
            if (crateSizes == null || crateSizes.Length == 0)
                throw new ArgumentException("짐 크기 후보가 없습니다.", nameof(crateSizes));

            _areaSize = areaSize;
            _sizes = crateSizes;
            _yawJitter = yawJitterDegrees;
            _offsetJitter = offsetJitter;
            _random = new System.Random(seed);

            // 가장 큰 짐이 한 칸에 들어가도록 열·행 수를 잡는다.
            float maxX = 0.01f;
            float maxZ = 0.01f;
            for (int i = 0; i < crateSizes.Length; i++)
            {
                maxX = Mathf.Max(maxX, crateSizes[i].x);
                maxZ = Mathf.Max(maxZ, crateSizes[i].z);
            }

            _columns = Mathf.Max(1, Mathf.FloorToInt(areaSize.x / maxX));
            _rows = Mathf.Max(1, Mathf.FloorToInt(areaSize.z / maxZ));
            _stackHeights = new float[_columns * _rows];
        }

        public int Placed => _placed;
        public int CellCount => _stackHeights.Length;

        /// <summary>다음 짐 자리를 정한다. 구역 높이에 더 올릴 수 없으면 false(짐 번호는 소모된다).</summary>
        public bool TryPlaceNext(out CargoPlacement placement)
        {
            int index = _placed++;
            int variant = index % _sizes.Length;
            Vector3 size = _sizes[variant];

            // 같은 난수 개수를 항상 쓰도록 먼저 뽑는다.
            float yaw = ((float)_random.NextDouble() * 2f - 1f) * _yawJitter;
            float offsetX = ((float)_random.NextDouble() * 2f - 1f) * _offsetJitter;
            float offsetZ = ((float)_random.NextDouble() * 2f - 1f) * _offsetJitter;

            int cell = -1;
            for (int i = 0; i < _stackHeights.Length; i++)
            {
                if (_stackHeights[i] + size.y > _areaSize.y)
                    continue;
                if (cell < 0 || _stackHeights[i] < _stackHeights[cell])
                    cell = i;
            }

            if (cell < 0)
            {
                placement = default;
                return false;
            }

            float cellX = _areaSize.x / _columns;
            float cellZ = _areaSize.z / _rows;
            float x = -_areaSize.x * 0.5f + cellX * (cell % _columns + 0.5f);
            float z = -_areaSize.z * 0.5f + cellZ * (cell / _columns + 0.5f);
            // 짐이 칸 밖으로 나가 이웃 짐·벽과 겹치지 않게 여유만큼만 흔든다.
            float slackX = Mathf.Max(0f, cellX * CellMargin - size.x) * 0.5f;
            float slackZ = Mathf.Max(0f, cellZ * CellMargin - size.z) * 0.5f;
            x += Mathf.Clamp(offsetX, -slackX, slackX);
            z += Mathf.Clamp(offsetZ, -slackZ, slackZ);

            placement = new CargoPlacement(
                new Vector3(x, _stackHeights[cell] + size.y * 0.5f, z), yaw, variant);
            _stackHeights[cell] += size.y;
            return true;
        }
    }
}
