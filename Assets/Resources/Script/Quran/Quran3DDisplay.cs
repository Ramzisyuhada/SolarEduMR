using UnityEngine;
using UnityEngine.UI;

public class Quran3DDisplay : MonoBehaviour
{
    [Header("UI Raw Images")]
    public RawImage ayatJudulRaw;
    public RawImage ayatRaw;
    public RawImage artiRaw;

    [Header("Audio (opsional)")]
    public AudioSource audioSource;

    [Header("Visibilitas Awal")]
    public bool hideOnAwake = false;

    void Awake()
    {
        if (hideOnAwake) Hide();
    }

    public void Show(QuranData data)
    {
        if (!data) { Hide(); return; }

        if (ayatJudulRaw)
        {
            if (data.ayatJudul != null) { ayatJudulRaw.texture = data.ayatJudul.texture; ayatJudulRaw.color = Color.white; ayatJudulRaw.gameObject.SetActive(true); }
            else { ayatJudulRaw.texture = null; ayatJudulRaw.color = new Color(1, 1, 1, 0); ayatJudulRaw.gameObject.SetActive(false); }
        }

        if (ayatRaw)
        {
            if (data.ayatSprite != null) { ayatRaw.texture = data.ayatSprite.texture; ayatRaw.color = Color.white; ayatRaw.gameObject.SetActive(true); }
            else { ayatRaw.texture = null; ayatRaw.color = new Color(1, 1, 1, 0); ayatRaw.gameObject.SetActive(false); }
        }

        if (artiRaw)
        {
            if (data.artiSprite != null) { artiRaw.texture = data.artiSprite.texture; artiRaw.color = Color.white; artiRaw.gameObject.SetActive(true); }
            else { artiRaw.texture = null; artiRaw.color = new Color(1, 1, 1, 0); artiRaw.gameObject.SetActive(false); }
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (ayatJudulRaw) { ayatJudulRaw.texture = null; ayatJudulRaw.color = new Color(1, 1, 1, 0); ayatJudulRaw.gameObject.SetActive(false); }
        if (ayatRaw) { ayatRaw.texture = null; ayatRaw.color = new Color(1, 1, 1, 0); ayatRaw.gameObject.SetActive(false); }
        if (artiRaw) { artiRaw.texture = null; artiRaw.color = new Color(1, 1, 1, 0); artiRaw.gameObject.SetActive(false); }
    }

    // Quran3DDisplay.cs
    public void StopAll()
    {
        var sources = GetComponentsInChildren<AudioSource>(true);
        foreach (var s in sources)
        {
            if (s.isPlaying) s.Stop();
            s.time = 0f;
            s.loop = false;
            s.clip = null; // opsional, tapi efektif mencegah “nyala” kembali
        }
    }


    public void AutoAssignCamera()
    {
        // Tambah logika jika pakai Canvas WorldSpace
    }
}
