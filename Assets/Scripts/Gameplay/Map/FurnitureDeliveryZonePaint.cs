using UnityEngine;

namespace GhostHunter.Gameplay.Map
{
    /// <summary>
    /// 반출 구역 바닥에 "칠한 페인트" 질감을 런타임에 만든다(<see cref="FurnitureDeliveryZoneView"/>). 에셋 파일 없이
    /// 구역 크기(m)에 맞춰 한 장을 그린다 — 가장자리를 두른 띠, 틈, 안쪽 칠, 그 위에 롤러 자국·벗겨진 곳·먼지 점.
    /// 알파가 있어 벗겨진 곳으로 바닥이 비쳐 스티커가 아니라 바닥에 칠한 표시로 읽힌다.
    ///
    /// <para>같은 크기면 항상 같은 그림이다(난수 대신 고정 오프셋 노이즈) — 접속자마다 모양이 같고 테스트가 안정적이다.
    /// 텍스처 좌표는 구역 로컬 (−X, −Z) 모서리가 (0, 0), (+X, +Z) 모서리가 (1, 1)이다.</para>
    /// </summary>
    internal static class FurnitureDeliveryZonePaint
    {
        internal const float PixelsPerMeter = 96f;
        internal const int MinResolution = 64;
        internal const int MaxResolution = 1024;

        // 가장자리 바깥선(빛나는 선) 안쪽부터의 띠 폭·틈·바깥 가장자리 흔들림(m).
        internal const float BandWidth = 0.3f;
        internal const float GapWidth = 0.1f;
        private const float EdgeWobble = 0.025f;
        // 칠한 색(sRGB) — 2026-11-03 사용자 지정 영역 내부색 #C65A20. 띠와 안쪽 칠이 같은 색이고, 칠이 온전한 곳은
        // 불투명해 화면에 이 색이 그대로 나온다(농도 얼룩은 밝기로만 흔든다 — shade).
        private static readonly Color BandPaint = new(0xC6 / 255f, 0x5A / 255f, 0x20 / 255f, 1f);
        private static readonly Color FillPaint = BandPaint;
        // 페인트가 벗겨진 곳에 드러나는 밑바탕 색(칠 색을 어둡게 가라앉힌 갈색).
        private static readonly Color Primer = new(0.40f, 0.20f, 0.11f, 1f);

        // 노이즈 오프셋 — 값은 임의의 고정 상수다.
        private const float OffsetWear = 31.7f;
        private const float OffsetMedium = 12.3f;
        private const float OffsetFine = 77.1f;
        private const float OffsetStreak = 5.9f;
        private const float OffsetEdge = 41.3f;

        /// <summary>구역 크기(m)에 맞는 페인트 텍스처를 만든다. 호출한 쪽이 <c>Destroy</c> 한다.</summary>
        internal static Texture2D CreateTexture(float sizeX, float sizeZ)
        {
            int width = ResolutionFor(sizeX);
            int height = ResolutionFor(sizeZ);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float mz = (y + 0.5f) / height * sizeZ;
                for (int x = 0; x < width; x++)
                {
                    float mx = (x + 0.5f) / width * sizeX;
                    pixels[y * width + x] = Paint(mx, mz, sizeX, sizeZ);
                }
            }

            // 멀리서 비스듬히 볼 때(서 있는 눈높이) 고운 노이즈가 반짝이지 않게 밉맵 + 이방성 필터.
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "DeliveryZonePaint",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        internal static int ResolutionFor(float meters)
        {
            return Mathf.Clamp(Mathf.RoundToInt(meters * PixelsPerMeter), MinResolution, MaxResolution);
        }

        /// <summary>구역 로컬 (mx, mz)m 한 점의 페인트 색. 알파가 칠의 두께(0 = 벗겨져 바닥이 그대로 보임).</summary>
        internal static Color32 Paint(float mx, float mz, float sizeX, float sizeZ)
        {
            // 가장자리까지의 거리. 모서리 흔들림은 칠의 바깥선이 자로 그은 듯 곧지 않게 한다.
            float edge = Mathf.Min(Mathf.Min(mx, sizeX - mx), Mathf.Min(mz, sizeZ - mz));
            edge += (Noise(mx * 9f, mz * 9f, OffsetEdge) - 0.5f) * 2f * EdgeWobble;

            // 띠(0~BandWidth)와 안쪽 칠(BandWidth+GapWidth~) 사이는 비어 있다.
            float soft = 1.5f / PixelsPerMeter;
            float band = Soft(edge, soft) * (1f - Soft(edge - BandWidth, soft));
            float fill = Soft(edge - BandWidth - GapWidth, soft);
            float coverage = Mathf.Max(band, fill);
            bool inBand = band >= fill;

            // 벗겨진 정도: 큰 얼룩 + 중간 얼룩 + 고운 알갱이.
            float wornNoise = Noise(mx, mz, OffsetWear) * 0.5f
                + Noise(mx * 5f, mz * 5f, OffsetMedium) * 0.35f
                + Noise(mx * 22f, mz * 22f, OffsetFine) * 0.15f;
            float worn = Smoothstep(0.52f, 0.78f, wornNoise);

            // 롤러 자국: 한 방향으로 길게 늘인 노이즈가 칠 농도를 완만한 줄무늬로 흔든다(너무 촘촘하면 주사선처럼 보인다).
            float streak = Noise(mx * 7f, mz * 0.9f, OffsetStreak);

            // 먼지·핀홀: 아주 가끔 찍히는 점.
            float speck = Hash(Mathf.FloorToInt(mx * PixelsPerMeter), Mathf.FloorToInt(mz * PixelsPerMeter));
            float pinhole = speck > 0.985f ? 0.3f : 1f;

            float thickness = coverage * (1f - 0.85f * worn) * pinhole;

            Color paint = inBand ? BandPaint : FillPaint;
            // 롤러 자국은 밝기로만 흔든다(평균 1.0) — 알파를 흔들면 바닥이 비쳐 지정색에서 벗어난다.
            float shade = 0.94f + 0.12f * streak - (speck < 0.03f ? 0.18f : 0f);
            Color color = Color.Lerp(paint * shade, Primer, worn * 0.8f);
            // 투명한 곳도 색을 남겨 둔다 — 밉맵·필터링 때 검정이 섞여 가장자리가 어두워지지 않게.
            color.a = Mathf.Clamp01(thickness);
            return color;
        }

        // HLSL smoothstep(from, to, value). Mathf.SmoothStep 은 value 가 아니라 t(0~1)를 받는 보간이라 쓰면 안 된다.
        private static float Smoothstep(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // 0~1 로 부드럽게 올라가는 문턱(폭 soft m).
        private static float Soft(float value, float soft)
        {
            return Mathf.Clamp01(value / soft + 0.5f);
        }

        private static float Noise(float x, float y, float offset)
        {
            return Mathf.Clamp01(Mathf.PerlinNoise(x + offset, y + offset));
        }

        // 격자 칸마다 고정된 0~1 값(정수 해시).
        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xFFFFu) / 65535f;
            }
        }
    }
}
