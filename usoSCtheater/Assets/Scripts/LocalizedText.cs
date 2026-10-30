using TMPro;
using UnityEngine;

namespace UsoSCTheater
{
    /// <summary>
    /// [다국어] 같은 오브젝트의 TMP 글자를 UITextData.xml의 Id 문구로 바꿈 (켜질 때·언어가 바뀔 때만 — Update 없음)
    /// 정적 UI 문구는 이 컴포넌트 + Id로만 (코드로 바꾸지 않음). 코드가 채우는 칸은 붙이지 않고 견본 글자를 '(견본) …'으로
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string textId;   // UITextData.xml의 Id (예: select.title)

        private TMP_Text label;

        public string TextId => textId;   //[검사] Check UI Texts용

        private void OnEnable()
        {
            Localization.Changed += Apply;
            Apply();
        }

        private void OnDisable() => Localization.Changed -= Apply;

        private void Apply()
        {
            if (string.IsNullOrWhiteSpace(textId))
            {
                Debug.LogWarning($"[LocalizedText] {name}: Id가 비어 있음 → 글자를 바꾸지 않음 (UITextData.xml의 Id를 입력)", this);
                return;
            }
            if (label == null) label = GetComponent<TMP_Text>();
            label.text = UIText.Get(textId);
        }
    }
}
