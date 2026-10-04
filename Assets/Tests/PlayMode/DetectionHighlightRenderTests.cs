using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 탐지(Q) 형광 덧입히기(mole-skill-system.md §4.5, MS-19) — 원래 머티리얼을 바꾸지 않고 같은 메시에 빛을 더하는지,
    /// 얼룩은 사각 메시가 아니라 얼룩 모양대로만 빛나는지 실제로 렌더해 확인한다.
    /// <c>GH_DETECTION_PNG_DIR</c> 환경 변수가 있으면 전후 장면을 PNG 로 저장한다.
    /// </summary>
    public sealed class DetectionHighlightRenderTests
    {
        private const int Width = 640;
        private const int Height = 360;

        private readonly List<Object> _created = new();
        private DetectionSkillSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = Track(ScriptableObject.CreateInstance<DetectionSkillSettings>());
        }

        [TearDown]
        public void TearDown()
        {
            DetectionTargetMarker.SetAllHighlighted(false, null);
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.Destroy(_created[i]);
            }

            _created.Clear();
        }

        [UnityTest]
        public IEnumerator 가구는_원래_머티리얼을_유지한_채_같은_메시로_형광을_덧그린다()
        {
            GameObject furniture = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            Renderer body = furniture.GetComponent<Renderer>();
            Material original = body.sharedMaterial;
            DetectionTargetMarker marker = furniture.AddComponent<DetectionTargetMarker>();
            yield return null;

            DetectionTargetMarker.SetAllHighlighted(true, _settings);
            yield return null;

            Assert.AreSame(original, body.sharedMaterial, "원래 머티리얼을 바꾸면 텍스처·음영이 사라진다.");
            Assert.IsTrue(marker.IsHighlighted);
            Transform glow = furniture.transform.Find("DetectionGlow");
            Assert.IsNotNull(glow, "덧그리기 렌더러가 없습니다.");
            Assert.AreSame(furniture.GetComponent<MeshFilter>().sharedMesh, glow.GetComponent<MeshFilter>().sharedMesh,
                "같은 메시를 써야 모양이 그대로다.");
            Assert.IsTrue(glow.GetComponent<MeshRenderer>().enabled);
            Assert.IsEmpty(glow.GetComponents<Collider>());

            body.enabled = false;
            yield return null;
            Assert.IsFalse(glow.GetComponent<MeshRenderer>().enabled, "원래 렌더러가 숨으면 덧그리기도 숨는다.");

            body.enabled = true;
            DetectionTargetMarker.SetAllHighlighted(false, null);
            Assert.IsFalse(glow.GetComponent<MeshRenderer>().enabled, "탐지가 끝나면 꺼진다.");
            Assert.IsFalse(marker.IsHighlighted);
        }

        [UnityTest]
        public IEnumerator 얼룩은_닦기_진행을_지우지_않고_자기_셰이더로_빛난다()
        {
            Renderer stain = CreateStain(Vector3.zero, out DetectionTargetMarker _);
            var block = new MaterialPropertyBlock();
            stain.GetPropertyBlock(block);
            block.SetFloat("_Wipe", 0.4f);
            stain.SetPropertyBlock(block);
            yield return null;

            DetectionTargetMarker.SetAllHighlighted(true, _settings);
            stain.GetPropertyBlock(block);

            Assert.IsNull(stain.transform.Find("DetectionGlow"), "얼룩은 사각 메시를 덧그리면 네모가 된다 — 셰이더가 직접 빛나야 한다.");
            Assert.AreEqual(0.4f, block.GetFloat("_Wipe"), 0.0001f, "닦기 진행이 지워졌다.");
            Assert.Greater(block.GetColor("_HighlightColor").a, 0.5f);

            DetectionTargetMarker.SetAllHighlighted(false, null);
            stain.GetPropertyBlock(block);
            Assert.AreEqual(0f, block.GetColor("_HighlightColor").a, 0.0001f);
            Assert.AreEqual(0.4f, block.GetFloat("_Wipe"), 0.0001f);
        }

        [UnityTest]
        public IEnumerator 렌더하면_가구는_밝아지고_얼룩은_모양대로만_빛난다()
        {
            var lightObject = Track(new GameObject("DimLight"));
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.35f;
            lightObject.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            GameObject furniture = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            furniture.transform.position = new Vector3(-1.1f, 0f, 0f);
            furniture.transform.rotation = Quaternion.Euler(15f, 35f, 0f);
            furniture.GetComponent<Renderer>().material.color = new Color(0.35f, 0.22f, 0.12f);
            furniture.AddComponent<DetectionTargetMarker>();

            Renderer stain = CreateStain(new Vector3(1.1f, 0f, 0f), out _);

            Camera camera = Track(new GameObject("DetectionCamera")).AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camera.transform.position = new Vector3(0f, 0f, -4f);
            camera.fieldOfView = 45f;
            yield return null;

            Texture2D before = Render(camera, "detection_before.png");
            DetectionTargetMarker.SetAllHighlighted(true, _settings);
            yield return null;
            Texture2D after = Render(camera, "detection_after.png");

            Rect furnitureRect = ScreenRect(camera, furniture.GetComponent<Renderer>().bounds);
            Assert.Greater(MeanLuminance(after, furnitureRect), MeanLuminance(before, furnitureRect) + 0.08f,
                "탐지 중 가구가 형광으로 밝아져야 합니다.");

            // 얼룩 사각 메시의 네 모서리(얼룩 모양 밖)는 탐지 중에도 배경 그대로여야 한다 — 네모로 빛나지 않는다.
            Rect stainRect = ScreenRect(camera, stain.bounds);
            float corner = CornerLuminance(after, stainRect);
            Assert.Less(corner, 0.06f, $"얼룩 사각 메시 모서리가 빛납니다(밝기 {corner:F3}) — 네모로 보인다.");
            Assert.Greater(MeanLuminance(after, Shrink(stainRect, 0.3f)), MeanLuminance(before, Shrink(stainRect, 0.3f)) + 0.1f,
                "얼룩 가운데는 형광으로 밝아져야 합니다.");
        }

        private Renderer CreateStain(Vector3 position, out DetectionTargetMarker marker)
        {
            GameObject stain = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
            stain.transform.position = position;
            Object.Destroy(stain.GetComponent<Collider>());
            Shader shader = Shader.Find("GhostHunter/CleaningStain");
            Assert.IsNotNull(shader, "GhostHunter/CleaningStain 셰이더를 찾지 못했습니다.");
            Renderer renderer = stain.GetComponent<Renderer>();
            renderer.sharedMaterial = Track(new Material(shader));

            marker = stain.AddComponent<DetectionTargetMarker>();
            typeof(DetectionTargetMarker)
                .GetField("_kind", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(marker, DetectionTargetKind.Stain);
            return renderer;
        }

        private Texture2D Render(Camera camera, string fileName)
        {
            var target = Track(new RenderTexture(Width, Height, 24));
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = Track(new Texture2D(Width, Height, TextureFormat.RGB24, false));
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string directory = System.Environment.GetEnvironmentVariable("GH_DETECTION_PNG_DIR");
            if (!string.IsNullOrEmpty(directory))
                File.WriteAllBytes(Path.Combine(directory, fileName), image.EncodeToPNG());
            return image;
        }

        private static Rect ScreenRect(Camera camera, Bounds bounds)
        {
            Vector3 min = new(float.MaxValue, float.MaxValue);
            Vector3 max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 p = camera.WorldToViewportPoint(corner);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            return Rect.MinMaxRect(min.x * Width, min.y * Height, max.x * Width, max.y * Height);
        }

        private static Rect Shrink(Rect rect, float keep)
        {
            Vector2 size = rect.size * keep;
            return new Rect(rect.center - size * 0.5f, size);
        }

        private static float MeanLuminance(Texture2D image, Rect rect)
        {
            float sum = 0f;
            int count = 0;
            for (int y = Mathf.Max(0, (int)rect.yMin); y < Mathf.Min(Height, (int)rect.yMax); y++)
            {
                for (int x = Mathf.Max(0, (int)rect.xMin); x < Mathf.Min(Width, (int)rect.xMax); x++)
                {
                    sum += image.GetPixel(x, y).grayscale;
                    count++;
                }
            }

            return count > 0 ? sum / count : 0f;
        }

        /// <summary>사각형 네 모서리 근처(각 변의 8%) 평균 밝기.</summary>
        private static float CornerLuminance(Texture2D image, Rect rect)
        {
            float w = rect.width * 0.08f;
            float h = rect.height * 0.08f;
            float sum = 0f;
            foreach (Vector2 corner in new[]
                     {
                         new Vector2(rect.xMin + 1f, rect.yMin + 1f), new Vector2(rect.xMax - w - 1f, rect.yMin + 1f),
                         new Vector2(rect.xMin + 1f, rect.yMax - h - 1f), new Vector2(rect.xMax - w - 1f, rect.yMax - h - 1f),
                     })
                sum += MeanLuminance(image, new Rect(corner, new Vector2(w, h)));
            return sum / 4f;
        }

        private T Track<T>(T target) where T : Object
        {
            _created.Add(target);
            return target;
        }
    }
}
