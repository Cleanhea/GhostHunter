using System.Collections;
using System.IO;
using GhostHunter.Gameplay.Map;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 반출 구역 꾸미기(<see cref="FurnitureDeliveryZoneView"/>)를 그래픽이 켜진 PlayMode 에서 어두운 밤 배경에 실제로 렌더해
    /// 초록 구역이 보이는지 확인한다. <c>GH_DELIVERY_VIEW_PNG</c> 환경 변수가 있으면 그 경로에 장면을 PNG 로 저장한다.
    /// </summary>
    public sealed class FurnitureDeliveryZoneRenderTests
    {
        private const int Width = 960;
        private const int Height = 540;

        [UnityTest]
        public IEnumerator 어두운_밤_화면에서_렌더하면_초록_구역이_보인다()
        {
            var zone = new GameObject("FurnitureDeliveryZone");
            zone.transform.position = new Vector3(0f, 1.25f, 0f);
            zone.AddComponent<FurnitureDeliveryZone>();
            zone.AddComponent<FurnitureDeliveryZoneView>();

            // 땅(바닥 y = 0)과 구역 옆 벽 하나 — 빛기둥이 벽에 가려지는지·땅에 얹히는지 보인다.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(30f, 0.5f, 30f);
            ground.GetComponent<Renderer>().material.color = new Color(0.07f, 0.08f, 0.09f);

            var cameraObject = new GameObject("DeliveryRenderCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f);
            // 서 있는 사람 눈높이(약 1.2m)에서 멀리 바라본 시점 — 서서 보는 사람에게 읽혀야 한다.
            camera.transform.position = new Vector3(-6f, 1.2f, -8f);
            camera.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            camera.fieldOfView = 60f;

            // Start 가 불려 표시가 만들어지고 한 프레임 그려질 때까지 기다린다.
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

            int green = 0;
            foreach (Color32 pixel in image.GetPixels32())
            {
                if (pixel.g > 60 && pixel.g > pixel.r + 15 && pixel.g > pixel.b + 15)
                    green++;
            }

            string dump = System.Environment.GetEnvironmentVariable("GH_DELIVERY_VIEW_PNG");
            if (!string.IsNullOrEmpty(dump))
                File.WriteAllBytes(dump, image.EncodeToPNG());

            Object.Destroy(zone);
            Object.Destroy(ground);
            Object.Destroy(cameraObject);
            Object.Destroy(target);
            Object.Destroy(image);

            Assert.Greater(green, 400, $"어두운 배경에서 초록 구역이 보여야 합니다(초록 픽셀 {green}개).");
        }
    }
}
