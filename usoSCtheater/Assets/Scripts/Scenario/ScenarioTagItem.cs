using TMPro;
using UnityEngine;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 시나리오 목록의 태그 1개 (TagItem 프리팹 루트).
    /// 배경 너비는 레이아웃(9-slice Image + HorizontalLayoutGroup)이 글자 길이에 맞춰 자동 조절.
    /// </summary>
    public class ScenarioTagItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        public void Setup(string text)
        {
            if (label == null)
            {
                Debug.LogWarning($"[ScenarioTagItem] {name}: Label 미연결");
                return;
            }
            label.text = text;
        }
    }
}
