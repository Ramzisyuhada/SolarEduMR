using UnityEngine;
using UnityEngine.UI;

public class PlanetInfoUI : MonoBehaviour
{
    [Header("UI")]
    public RawImage infoRaw;        // gambar full (nama+deskripsi)
    public RawImage planetRaw;      // gambar planet opsional

    [Header("Audio (opsional)")]
    public AudioSource audioSource;

    public void Show(PlanetData d)
    {
        if (!d) { Hide(); return; }

        if (infoRaw) infoRaw.texture = d.infoImage ? d.infoImage.texture : null;
        if (planetRaw) planetRaw.texture = d.planetPicture ? d.planetPicture.texture : null;

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (infoRaw) infoRaw.texture = null;
        if (planetRaw) planetRaw.texture = null;
        gameObject.SetActive(false);
    }

    public void PlayNarration(PlanetData d)
    {
        if (!audioSource || !d || !d.narration) return;
        audioSource.Stop();
        audioSource.clip = d.narration;
        audioSource.Play();
    }
}
