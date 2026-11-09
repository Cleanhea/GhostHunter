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
    /// 주황 페인트(#C65A20) 구역과 흰 외곽선·중앙 심볼(#FFFFFF)이 보이는지 확인한다. <c>GH_DELIVERY_VIEW_PNG</c> 환경 변수가 있으면 그 경로에 장면을 PNG 로 저장한다
    /// (<c>GH_DELIVERY_VIEW_CLOSE_PNG</c> 는 구역 옆 가까운 시점, <c>GH_DELIVERY_VIEW_SYMBOL_PNG</c> 는 위에서 본 심볼).
    /// </summary>
    public sealed class FurnitureDeliveryZoneRenderTests
    {
        private const int Width = 960;
        private const int Height = 540;

        [UnityTest]
        public IEnumerator 어두운_밤_화면에서_렌더하면_주황_페인트와_흰_외곽선이_보인다()
        {
            var zone = new GameObject("FurnitureDeliveryZone");
            zone.transform.position = new Vector3(0f, 1.25f, 0f);
            zone.AddComponent<FurnitureDeliveryZone>();
            zone.AddComponent<FurnitureDeliveryZoneView>();

            // 땅(바닥 y = 0) — 페인트가 땅에 얹혀 보이는지 본다.
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

            Texture2D image = Capture(camera);
            CountPixels(image, out int paint, out int white);
            Dump("GH_DELIVERY_VIEW_PNG", image);

            // 구역 옆에서 내려다본 가까운 시점 — 서 있는 눈높이에서는 구역이 비스듬히 납작해 외곽선이 대부분이라,
            // 외곽선·빛무리·중앙 심볼과 함께 주황 칠이 넓게 보이는지는 여기서 본다.
            camera.transform.position = new Vector3(-3.5f, 2.6f, -5.5f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0f));
            Texture2D close = Capture(camera);
            CountPixels(close, out int closePaint, out int closeWhite);
            Dump("GH_DELIVERY_VIEW_CLOSE_PNG", close);

            // 테두리가 들어오지 않는 화면 중앙을 검사해 심볼 자체가 바닥 칠 위에 그려지는지 확인한다.
            camera.transform.position = new Vector3(0f, 7f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.orthographic = true;
            camera.orthographicSize = 3f;
            Texture2D overview = Capture(camera);
            int symbolWhite = 0;
            foreach (Color pixel in overview.GetPixels(Width / 2 - 96, Height / 2 - 96, 192, 192))
            {
                if (pixel.r > 0.78f && pixel.g > 0.78f && pixel.b > 0.78f)
                    symbolWhite++;
            }
            Dump("GH_DELIVERY_VIEW_SYMBOL_PNG", overview);

            Object.Destroy(zone);
            Object.Destroy(ground);
            Object.Destroy(cameraObject);
            Object.Destroy(image);
            Object.Destroy(close);
            Object.Destroy(overview);

            Assert.Greater(paint, 400, $"어두운 배경에서 주황 구역이 보여야 합니다(주황 픽셀 {paint}개).");
            Assert.Greater(white, 20, $"흰 외곽선이 보여야 합니다(흰 픽셀 {white}개).");
            Assert.Greater(closePaint, 5000, $"가까이서는 주황 페인트가 넓게 보여야 합니다(주황 픽셀 {closePaint}개).");
            Assert.Less(closeWhite, closePaint,
                $"흰 외곽선·심볼보다 주황 칠이 넓게 보여야 합니다(흰 {closeWhite} / 주황 {closePaint}).");
            Assert.Greater(symbolWhite, 300, $"테두리 안쪽 중앙에 흰 심볼이 보여야 합니다(흰 {symbolWhite}개).");
        }

        // 주황(#C65A20 계열, R 이 G 의 1.4배 이상·B 의 3배 이상) 픽셀 수와 흰(세 채널 모두 200 초과) 픽셀 수.
        private static void CountPixels(Texture2D image, out int paint, out int white)
        {
            paint = 0;
            white = 0;
            foreach (Color32 pixel in image.GetPixels32())
            {
                if (pixel.r > 60 && pixel.r > pixel.g * 1.4f && pixel.r > pixel.b * 3f)
                    paint++;
                else if (pixel.r > 200 && pixel.g > 200 && pixel.b > 200)
                    white++;
            }
        }

        private static void Dump(string environmentVariable, Texture2D image)
        {
            string path = System.Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrEmpty(path))
                File.WriteAllBytes(path, image.EncodeToPNG());
        }

        private static Texture2D Capture(Camera camera)
        {
            var target = new RenderTexture(Width, Height, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            Object.Destroy(target);
            return image;
        }
    }
}
