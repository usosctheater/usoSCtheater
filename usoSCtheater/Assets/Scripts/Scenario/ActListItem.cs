using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 막 목록의 한 행. ActListItem 프리팹과 ScenarioListItem 안의 ActListItem_All이 공용으로 사용.
    /// PlayButton 클릭 시 바로 재생(onPlay)한다. (OnClick은 인스펙터에서 연결하지 않음 — 중복 실행 방지)
    /// </summary>
    public class ActListItem : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI actTitle;
        [SerializeField] private Button playButton;

        //title이 null이면 프리팹에 적힌 문구 유지 (ActListItem_All용)
        public void Setup(string title, Action onPlay)
        {
            if (title != null && actTitle != null) actTitle.text = title;

            if (playButton == null)
            {
                Debug.LogWarning($"[ActListItem] {name}: PlayButton 미연결");
                return;
            }
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(() => onPlay?.Invoke());
        }
    }
}
