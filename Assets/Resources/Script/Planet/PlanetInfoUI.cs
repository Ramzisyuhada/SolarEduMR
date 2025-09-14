using UnityEngine;
using UnityEngine.UI;

public class PlanetInfoUI : MonoBehaviour
{
    [Header("UI Planet")]
    public Image planetDescImage;   // Sprite dari PlanetData.infoImage

    [Header("Audio")]
    public AudioSource descSource;  // narasi planet

    // Tampilkan info planet + audio
    public void Show(PlanetData planet)
    {
        if (planetDescImage)
            planetDescImage.sprite = planet ? planet.infoImage : null;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        // Mainkan audio narasi jika ada
        if (planet && planet.narration)
            PlayClip(descSource, planet.narration);
    }

    // Sembunyikan info planet
    public void HidePlanet()
    {
        if (planetDescImage)
            planetDescImage.sprite = null;
        if (descSource)
            descSource.Stop();
    }

    public void HideAll()
    {
        HidePlanet();
        gameObject.SetActive(false);
    }

    // === Audio helper ===
    public void PlayDescription(PlanetData d)
    {
        if (d && d.narration)
            PlayClip(descSource, d.narration);
    }

    void PlayClip(AudioSource src, AudioClip clip)
    {
        if (!src || !clip) return;
        src.Stop();
        src.clip = clip;
        src.Play();
    }
}
