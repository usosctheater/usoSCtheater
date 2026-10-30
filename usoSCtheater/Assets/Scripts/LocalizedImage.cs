using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UsoSCTheater
{
    /// <summary>
    /// [다국어] 씬·프리팹이 직접 참조하는 Image의 언어별 Sprite. 인스펙터의 Image Sprite = 기본 언어(한국어), 다른 언어는 목록에 (없으면 기본)
    /// 켜질 때·언어가 바뀔 때만 갱신 (Update 없음). 코드가 sprite를 바꾸는 Image에는 붙이지 않음. 빠뜨림은 Check Translations가 알려 줌
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class LocalizedImage : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string language;   // LanguageData 코드 (예: ja)
            public Sprite sprite;
        }

        [SerializeField] private List<Entry> sprites = new List<Entry>();

        private Image image;
        private Sprite defaultSprite;

        public IReadOnlyList<Entry> Sprites => sprites;   //[검사] Check Translations용

        private void Awake()
        {
            image = GetComponent<Image>();
            defaultSprite = image.sprite;   // 인스펙터에 넣은 한국어 이미지
        }

        private void OnEnable()
        {
            Localization.Changed += Apply;
            Apply();
        }

        private void OnDisable() => Localization.Changed -= Apply;

        private void Apply()
        {
            Sprite chosen = defaultSprite;
            string code = Localization.Current.Code;
            foreach (var entry in sprites)
            {
                if (entry.sprite != null && string.Equals(entry.language?.Trim(), code, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = entry.sprite;
                    break;
                }
            }
            image.sprite = chosen;
        }
    }
}
