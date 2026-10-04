using System.Collections;
using System.IO;
using GhostHunter.Gameplay.Revival;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 바닥 마법진 스프라이트를 그래픽이 켜진 PlayMode 에서 어두운 방 바닥에 실제로 렌더해 붉은 선이 보이는지 확인한다.
    /// <c>GH_REVIVAL_CIRCLE_PNG</c> 환경 변수가 있으면 그 경로에 장면을 PNG 로 저장한다.
    /// </summary>
    public sealed class RevivalCircleRenderTests
    {
        private const int Width = 960;
        private const int Height = 540;

        [UnityTest]
        public IEnumerator 어두운_바닥에_렌더하면_마법진_선이_보인다()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(30f, 0.5f, 30f);
            ground.GetComponent<Renderer>().material.color = new Color(0.07f, 0.08f, 0.09f);

            // RevivalRitual.BuildVisuals 와 같은 배치 — 바닥에서 1.2cm 위에 눕힌 스프라이트.
            var circle = new GameObject("MagicCircle");
            circle.transform.SetPositionAndRotation(new Vector3(0f, 0.012f, 0f), Quaternion.Euler(90f, 0f, 0f));
            circle.transform.localScale = Vector3.one * (1.45f / RevivalCircleTexture.StarRadius);
            SpriteRenderer sprite = circle.AddComponent<SpriteRenderer>();
            sprite.sprite = RevivalCircleTexture.Sprite;
            sprite.color = new Color(1f, 1f, 1f, 0.9f);

            var cameraObject = new GameObject("CircleRenderCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
            // 원 가장자리 밖 서 있는 눈높이에서 가운데를 바라본 시점.
            camera.transform.position = new Vector3(0f, 1.6f, -3.2f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0f));
            camera.fieldOfView = 60f;

            yield return null;
            yield return null;

            var target = new RenderTexture(Width, Height, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            int red = 0;
            foreach (Color32 pixel in image.GetPixels32())
            {
                if (pixel.r > 120 && pixel.r > pixel.g + 50 && pixel.r > pixel.b + 50)
                    red++;
            }

            string dump = System.Environment.GetEnvironmentVariable("GH_REVIVAL_CIRCLE_PNG");
            if (!string.IsNullOrEmpty(dump))
                File.WriteAllBytes(dump, image.EncodeToPNG());

            Object.Destroy(ground);
            Object.Destroy(circle);
            Object.Destroy(cameraObject);
            Object.Destroy(target);
            Object.Destroy(image);

            Assert.Greater(red, 400, $"어두운 바닥에서 붉은 마법진 선이 보여야 합니다(붉은 픽셀 {red}개).");
        }
    }
}
