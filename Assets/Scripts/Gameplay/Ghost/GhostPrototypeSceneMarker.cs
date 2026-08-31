using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>스폰된 귀신의 위치를 Scene 뷰에서만 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class GhostPrototypeSceneMarker : MonoBehaviour
    {
        private static readonly Color MarkerColor = new(0.5f, 0.8f, 1f, 0.9f);

        private void OnDrawGizmos()
        {
            Gizmos.color = MarkerColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.55f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, transform.forward * 0.75f);
        }
    }
}
