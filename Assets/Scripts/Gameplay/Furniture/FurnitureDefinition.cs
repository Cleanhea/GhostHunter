using UnityEngine;

namespace GhostHunter.Gameplay.Furniture
{
    public enum FurnitureWeightClass
    {
        Light,
        Heavy,
    }

    [CreateAssetMenu(
        fileName = "FurnitureDefinition_New",
        menuName = "GhostHunter/Furniture Definition")]
    public sealed class FurnitureDefinition : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float _mass = 8f;
        [SerializeField] private FurnitureWeightClass _weightClass = FurnitureWeightClass.Light;

        public float Mass => _mass;
        public FurnitureWeightClass WeightClass => _weightClass;
        public bool IsHeavy => _weightClass == FurnitureWeightClass.Heavy;
    }
}
