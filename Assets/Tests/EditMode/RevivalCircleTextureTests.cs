using System.IO;
using GhostHunter.Gameplay.Revival;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 바닥 마법진 텍스처 — 오망성 꼭짓점이 촛대 방향에 그려지고 가운데(시체 자리)는 비어 있다.
    /// 확인용 그림을 <c>Logs/tests/RevivalCircle.png</c> 에 남긴다(생성물 폴더).
    /// </summary>
    public sealed class RevivalCircleTextureTests
    {
        private const int Size = 256;
        private Texture2D _texture;

        [OneTimeSetUp]
        public void Build() => _texture = RevivalCircleTexture.Build(Size, keepReadable: true);

        [OneTimeTearDown]
        public void Cleanup()
        {
            if (_texture != null)
                Object.DestroyImmediate(_texture);
        }

        [Test]
        public void 오망성_꼭짓점은_촛대_방향에_그려진다()
        {
            for (int i = 0; i < RevivalRules.CandleCount; i++)
            {
                float angle = RevivalCircleTexture.VertexAngle(i);
                Vector2 uv = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RevivalCircleTexture.StarRadius * 0.5f
                    + new Vector2(0.5f, 0.5f);
                // 꼭짓점은 두 선이 뾰족하게 만나는 끝이라 한 점 샘플은 불안정하다 — 주변 3×3 텍셀의 최댓값을 본다.
                int cx = Mathf.FloorToInt(uv.x * Size);
                int cy = Mathf.FloorToInt(uv.y * Size);
                float alpha = 0f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        alpha = Mathf.Max(alpha, _texture.GetPixel(cx + dx, cy + dy).a);
                Assert.Greater(alpha, 0.5f, $"꼭짓점 {i} ({uv}) 가 비었다");
            }
        }

        [Test]
        public void 가운데와_바깥은_비어_있다()
        {
            Assert.Less(_texture.GetPixelBilinear(0.5f, 0.5f).a, 0.1f, "가운데 — 시체 자리");
            Assert.Less(_texture.GetPixel(0, 0).a, 0.05f, "원 바깥 모서리");
        }

        [Test]
        public void 확인용_그림을_남긴다()
        {
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "tests");
            Directory.CreateDirectory(directory);
            Texture2D preview = RevivalCircleTexture.Build(512, keepReadable: true);
            File.WriteAllBytes(Path.Combine(directory, "RevivalCircle.png"), preview.EncodeToPNG());
            Object.DestroyImmediate(preview);
            Assert.Pass();
        }
    }
}
