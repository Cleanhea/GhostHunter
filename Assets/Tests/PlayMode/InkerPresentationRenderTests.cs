using System.Collections;
using System.IO;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 인커 연출(사용자 요청 2026-11-15)을 그래픽이 켜진 PlayMode 에서 실제로 렌더한다.
    /// <list type="bullet">
    /// <item>피 글씨: 어두운 벽에 셰이더 GhostHunter/InkerBlood 로 검붉게 그려지는지(분홍 오류·하늘색 임시 셰이더 없음), 쓰는 중에는 일부만 그려지는지.</item>
    /// <item>화면 분위기: 포스트프로세싱이 화면 가장자리를 어둡게 하고 채도를 빼는지.</item>
    /// </list>
    /// 환경 변수 <c>GH_INKER_RENDER_DIR</c> 가 있으면 그 폴더에 장면 PNG 를 저장한다(눈으로 확인용).
    /// </summary>
    public sealed class InkerPresentationRenderTests
    {
        private const int Width = 960;
        private const int Height = 540;

        [UnityTest]
        public IEnumerator 피_글씨는_어두운_벽에_검붉게_그려지고_쓰는_중에는_일부만_보인다()
        {
            GameObject wall = CreateWall();
            Camera camera = CreateCamera(new Vector3(0f, 1.45f, -2.2f), postProcessing: false);
            InkerStrokeFont.Size(InkerCommandText.Of(InkerCommand.DontTouchFurniture), 0.32f, 2.4f,
                out float width, out float height, out float wrap, out float letter);
            var state = new InkerWritingState
            {
                Id = 11,
                Command = InkerCommand.DontTouchFurniture,
                LocalPosition = new Vector3(0f, 1.45f, -0.015f),
                LocalRotation = Quaternion.LookRotation(Vector3.back),
                Width = width,
                Height = height,
                LetterHeight = letter,
                WrapWidth = wrap,
                RevealStartTime = 0d,
                RevealSeconds = 4f,
                WipeRemaining = 6,
                WipeRequired = 6,
            };
            InkerWritingView view = InkerWritingView.Create(state);
            yield return null;

            view.Refresh(null, 1.6d);
            Texture2D partial = Capture(camera);
            Count(partial, out int partialBlood, out int partialError);
            Dump("inker_blood_partial.png", partial);

            state.Complete = true;
            view.Apply(state);
            view.Refresh(null, 30d);
            Texture2D full = Capture(camera);
            Count(full, out int fullBlood, out int fullError);
            Dump("inker_blood_full.png", full);

            // 가까이서 본 획·흘러내림 세부(눈으로 확인용).
            camera.transform.position = new Vector3(-0.45f, 1.7f, -0.75f);
            Texture2D close = Capture(camera);
            Dump("inker_blood_close.png", close);
            Object.Destroy(close);
            camera.transform.position = new Vector3(0f, 1.45f, -2.2f);

            // 닦다 만 글씨 — 번지고 흐려진다.
            state.WipeRemaining = 2;
            view.Apply(state);
            view.Refresh(null, 30d);
            Texture2D wiped = Capture(camera);
            Count(wiped, out int wipedBlood, out _);
            Dump("inker_blood_wiped.png", wiped);

            Object.Destroy(view.gameObject);
            Object.Destroy(wall);
            Object.Destroy(camera.gameObject);
            Object.Destroy(partial);
            Object.Destroy(full);
            Object.Destroy(wiped);

            Assert.AreEqual(0, fullError + partialError, "셰이더 오류(분홍)·임시 셰이더(하늘색) 픽셀이 없어야 한다");
            Assert.Greater(fullBlood, 6000, $"다 쓴 글씨가 검붉게 보여야 한다(피 픽셀 {fullBlood})");
            Assert.Greater(partialBlood, 300, $"쓰는 중에도 앞부분은 보인다(피 픽셀 {partialBlood})");
            Assert.Less(partialBlood, fullBlood * 0.8f, "쓰는 중에는 일부만 보인다");
            Assert.Less(wipedBlood, fullBlood, "닦을수록 흐려진다");
        }

        [UnityTest]
        public IEnumerator 화면_분위기는_가장자리를_어둡게_하고_채도를_뺀다()
        {
            GameObject wall = CreateWall(new Color(0.55f, 0.5f, 0.42f));
            Camera camera = CreateCamera(new Vector3(0f, 1.45f, -2.2f), postProcessing: true);
            yield return null;

            Texture2D before = Capture(camera);
            InkerAtmosphere atmosphere = InkerAtmosphere.Create();
            atmosphere.SetTarget(1f, 1f);
            float waited = 0f;
            while (atmosphere.AmbientWeight < 0.99f && waited < 3f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            Texture2D after = Capture(camera);
            Dump("inker_atmosphere_off.png", before);
            Dump("inker_atmosphere_on.png", after);

            atmosphere.Shock(1f, 0.8f);
            yield return null;
            float shockStart = atmosphere.ShockWeight;
            Texture2D shocked = Capture(camera);
            Dump("inker_atmosphere_shock.png", shocked);
            waited = 0f;
            while (waited < 1.5f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            float shockLater = atmosphere.ShockWeight;
            atmosphere.SetTarget(0f, 1f);
            waited = 0f;
            while (atmosphere.AmbientWeight > 0.01f && waited < 4f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            float faded = atmosphere.AmbientWeight;
            float cornerBefore = Luminance(before, 6, 6, 60);
            float cornerAfter = Luminance(after, 6, 6, 60);
            float centerSatBefore = Saturation(before, Width / 2 - 40, Height / 2 - 40, 80);
            float centerSatAfter = Saturation(after, Width / 2 - 40, Height / 2 - 40, 80);

            Object.Destroy(atmosphere.gameObject);
            Object.Destroy(wall);
            Object.Destroy(camera.gameObject);
            Object.Destroy(before);
            Object.Destroy(after);
            Object.Destroy(shocked);

            Assert.Greater(waited, 0f);
            Assert.Less(cornerAfter, cornerBefore * 0.8f, $"비네트 — 모서리가 어두워진다({cornerBefore:0.000} → {cornerAfter:0.000})");
            Assert.Less(centerSatAfter, centerSatBefore, $"채도가 빠진다({centerSatBefore:0.000} → {centerSatAfter:0.000})");
            Assert.Greater(shockStart, 0.8f, "충격은 곧바로 세다");
            Assert.Less(shockLater, 0.15f, "충격은 금방 사라진다");
            Assert.Less(faded, 0.02f, "현상이 끝나면 분위기가 풀린다");
        }

        // ───────── 도우미 ─────────

        private static GameObject CreateWall(Color? color = null)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "InkerRenderWall";
            wall.transform.position = new Vector3(0f, 1.5f, 0.1f);
            wall.transform.localScale = new Vector3(8f, 4f, 0.2f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color ?? new Color(0.16f, 0.15f, 0.14f);
            wall.GetComponent<Renderer>().sharedMaterial = material;
            return wall;
        }

        private static Camera CreateCamera(Vector3 position, bool postProcessing)
        {
            var cameraObject = new GameObject("InkerRenderCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            camera.fieldOfView = 60f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = postProcessing;
            return camera;
        }

        // 피 = 붉은 기가 뚜렷한 어두운 빨강. 오류 = 분홍(셰이더 오류)·하늘색(아직 컴파일 중인 임시 셰이더).
        private static void Count(Texture2D image, out int blood, out int error)
        {
            blood = 0;
            error = 0;
            foreach (Color32 pixel in image.GetPixels32())
            {
                if (pixel.r > 40 && pixel.r > pixel.g * 2.2f && pixel.r > pixel.b * 2.2f)
                    blood++;
                else if ((pixel.r > 150 && pixel.b > 150 && pixel.g < 90) || (pixel.g > 150 && pixel.b > 150 && pixel.r < 90))
                    error++;
            }
        }

        private static float Luminance(Texture2D image, int x, int y, int size)
        {
            float sum = 0f;
            Color[] pixels = image.GetPixels(x, y, size, size);
            foreach (Color pixel in pixels)
                sum += pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f;
            return sum / pixels.Length;
        }

        private static float Saturation(Texture2D image, int x, int y, int size)
        {
            float sum = 0f;
            Color[] pixels = image.GetPixels(x, y, size, size);
            foreach (Color pixel in pixels)
            {
                Color.RGBToHSV(pixel, out _, out float s, out _);
                sum += s;
            }

            return sum / pixels.Length;
        }

        private static void Dump(string fileName, Texture2D image)
        {
            string directory = System.Environment.GetEnvironmentVariable("GH_INKER_RENDER_DIR");
            if (string.IsNullOrEmpty(directory))
                return;
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), image.EncodeToPNG());
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
