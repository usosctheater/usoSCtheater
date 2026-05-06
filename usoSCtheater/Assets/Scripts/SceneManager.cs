using UnityEngine;

public class SceneManager : MonoBehaviour
{
    public static SceneManager instance;
    
    [SerializeField] DialogManager dialogManager;
    [SerializeField] UIManager uiManager;
    [SerializeField] BGManager bgManager;
    [SerializeField] CGManager cgManager;

    // public void Nextline() {
    //     var line = scriptData.lines[currentIndex];
    //     dialogueManager.ShowText(line.text);
    //     uiManager.SetNameBox(line.speaker);
    //     bgManager.SetBG(line.bgKey);
    //     if (!string.IsNullOrEmpty(line.cgKey))
    //         cgManager.ShowCG(line.cgKey);
    //     currentIndex++;
    // }
}
