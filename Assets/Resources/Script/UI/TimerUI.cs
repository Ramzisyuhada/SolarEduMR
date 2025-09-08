using UnityEngine;
using TMPro;

public class TimerUI : MonoBehaviour
{
    public SolarGameManager manager;
    public TMP_Text timerText;

    void Update()
    {
        if (!manager) return;
        var t = Mathf.Max(0, manager.timeLeft.Value);
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        timerText.text = $"{m:00}:{s:00}";
    }
}
