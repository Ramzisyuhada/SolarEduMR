using UnityEngine;
using UnityEngine.UI;

public class Quran3DDisplay : MonoBehaviour
{
    [Header("UI")]
    public Image ayatJudulText;
    public Image ayatImage;
    public Image artiImage;

    [Header("Audio (opsional)")]
    public AudioSource ayatSource;
    public AudioSource artiSource;
    [Range(0f, 1f)] public float masterVolume = 1f;
    public bool autoPlayOnShow = false;

    [Header("Look & Feel")]
    public Transform billboardTarget;   // Camera.main.transform (opsional)
    public float bobAmplitude = 0.01f;
    public float bobSpeed = 1.2f;
    public float faceCameraLerp = 8f;

    QuranData _current;
    Vector3 _startLocalPos;
    float _t;

    void Awake()
    {
        _startLocalPos = transform.localPosition;
        ApplyVolume();

        if (ayatSource) ayatSource.playOnAwake = false;
        if (artiSource) artiSource.playOnAwake = false;
    }

    public void Show(QuranData data)
    {
        _current = data;

        if (ayatJudulText) ayatJudulText.sprite = data ? data.ayatJudul : null;
        if (ayatImage) ayatImage.sprite = data ? data.ayatSprite : null;
        if (artiImage) artiImage.sprite = data ? data.artiSprite : null;

        gameObject.SetActive(true);

        if (autoPlayOnShow && _current != null)
        {
            PlayAyatAt(0);
            PlayArtiAt(0);
        }
    }

    public void Hide()
    {
        StopAll();
        gameObject.SetActive(false);
    }

    public void PlayAyat()
    {
        PlayAyatAt(0);
    }

    public void PlayArti()
    {
        PlayArtiAt(0);
    }

    public void PlayAyatAt(float offsetSeconds)
    {
        if (!_current || !ayatSource || !_current.ayatAudio) return;

        ayatSource.Stop();
        ayatSource.clip = _current.ayatAudio;

        // Mulai dari offset yang sama antar klien
        float len = ayatSource.clip.length;
        float t = len > 0 ? Mathf.Repeat(offsetSeconds, len) : 0f;
#if UNITY_WEBGL
        ayatSource.time = t; // timeSamples kurang stabil di WebGL
#else
        ayatSource.time = t;
        // Alternatif lebih presisi:
        // ayatSource.timeSamples = Mathf.Clamp((int)(t * ayatSource.clip.frequency), 0, ayatSource.clip.samples - 1);
#endif
        ayatSource.Play();
    }

    public void PlayArtiAt(float offsetSeconds)
    {
        if (!_current || !artiSource || !_current.artiAudio) return;

        artiSource.Stop();
        artiSource.clip = _current.artiAudio;

        float len = artiSource.clip.length;
        float t = len > 0 ? Mathf.Repeat(offsetSeconds, len) : 0f;
#if UNITY_WEBGL
        artiSource.time = t;
#else
        artiSource.time = t;
        // artiSource.timeSamples = Mathf.Clamp((int)(t * artiSource.clip.frequency), 0, artiSource.clip.samples - 1);
#endif
        artiSource.Play();
    }

    public void StopAll()
    {
        if (ayatSource) ayatSource.Stop();
        if (artiSource) artiSource.Stop();
    }

    public void ApplyVolume()
    {
        if (ayatSource) ayatSource.volume = masterVolume;
        if (artiSource) artiSource.volume = masterVolume;
    }

    public void AutoAssignCamera()
    {
        if (!billboardTarget && Camera.main)
            billboardTarget = Camera.main.transform;
    }
}
