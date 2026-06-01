using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class DividerRectSettings
{
    public Sprite targetSprite;
    public float posX;
    public float posY;
    public float width;
    public float height;
}

public class TransitionDividerItem : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image dividerImage;

    [SerializeField] private RectTransform dividerRect;
    [SerializeField] private List<DividerRectSettings> rectSettingsList;

    public void Setup(Sprite sprite, UnityEngine.UI.Image.Type imageType)
    {
        dividerImage.sprite = sprite;
        dividerImage.type = imageType;

        //이미지에 맞는 RectTransform 설정 적용
        if (dividerRect != null && rectSettingsList != null)
        {
            foreach (var settings in rectSettingsList)
            {
                if (settings.targetSprite == sprite)
                {
                    dividerRect.anchoredPosition = new Vector2(settings.posX, settings.posY);
                    dividerRect.sizeDelta = new Vector2(settings.width, settings.height);
                    break;
                }
            }
        }
    }
}
