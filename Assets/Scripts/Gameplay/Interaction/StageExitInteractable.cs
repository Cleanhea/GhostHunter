using GhostHunter.Gameplay.Ghost;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>드릴카 안의 스테이지 종료 장치. 각 피어가 같은 위치에 로컬 표시물을 만든다.</summary>
    [DisallowMultipleComponent]
    public sealed class StageExitInteractable : MonoBehaviour
    {
        public static void CreateInDrillCar()
        {
            DrillCarSafeZone zone = FindFirstObjectByType<DrillCarSafeZone>();
            if (zone == null || FindFirstObjectByType<StageExitInteractable>() != null)
                return;

            GameObject terminal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            terminal.name = "StageExitTerminal";
            terminal.transform.SetParent(zone.transform, false);
            terminal.transform.localPosition = new Vector3(0f,
                -zone.Size.y * 0.5f + 1.3f, zone.Size.z * 0.5f - 0.3f);
            terminal.transform.localScale = new Vector3(0.5f, 0.5f, 0.15f);
            terminal.GetComponent<Renderer>().material.color = Color.red;
            terminal.AddComponent<StageExitInteractable>();
        }
    }
}
