using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 레이어 이름/인덱스를 한 곳에 모은다. 레이어 인덱스는 프로젝트 설정에 따라 달라지므로
    /// 코드에 숫자로 박으면 설정이 바뀌는 순간 조용히 엉뚱한 레이어를 가리키게 된다.
    /// </summary>
    public static class GameLayers
    {
        public const string PlayerName = "Player";
        public const string FurnitureName = "Furniture";

        /// <summary>Project Settings > Tags and Layers 에 아직 만들지 않았으면 -1.</summary>
        public static int Player { get; private set; }
        public static int Furniture { get; private set; }

        /// <summary>가구 타겟팅 레이캐스트용 마스크. 레이어가 없으면 0(아무것도 안 맞음).</summary>
        public static LayerMask FurnitureMask { get; private set; }

        static GameLayers()
        {
            Player = LayerMask.NameToLayer(PlayerName);
            Furniture = LayerMask.NameToLayer(FurnitureName);

            FurnitureMask = Furniture >= 0 ? 1 << Furniture : 0;

            // 레이어를 만들기 전에 조용히 동작하면 "레이캐스트가 아무것도 안 맞는" 원인을
            // 찾느라 시간을 버린다. 시작할 때 한 번 크게 알린다.
            WarnIfMissing(Player, PlayerName);
            WarnIfMissing(Furniture, FurnitureName);
        }

        private static void WarnIfMissing(int layer, string name)
        {
            if (layer < 0)
            {
                Debug.LogError(
                    $"[GameLayers] '{name}' 레이어가 없습니다. " +
                    "Project Settings > Tags and Layers 에서 추가하세요.");
            }
        }
    }
}
