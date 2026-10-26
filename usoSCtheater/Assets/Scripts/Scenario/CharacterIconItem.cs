using UnityEngine;
using UnityEngine.UI;

namespace UsoSCTheater.Scenario
{
    /// <summary>시나리오 목록의 등장인물 아이콘 1개 (CharacterIcon 프리팹 루트)</summary>
    public class CharacterIconItem : MonoBehaviour
    {
        [SerializeField] private Image icon;

        public string CharacterId { get; private set; }

        public void Setup(string characterId, Sprite sprite)
        {
            CharacterId = characterId;
            if (icon == null)
            {
                Debug.LogWarning($"[CharacterIconItem] {name}: Icon 미연결");
                return;
            }
            icon.sprite = sprite;
        }
    }
}
