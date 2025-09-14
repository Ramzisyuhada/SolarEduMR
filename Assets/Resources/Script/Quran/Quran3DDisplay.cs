using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    }

    void Update()
    {
        // bobbing
        //_t += Time.deltaTime * bobSpeed;
        //var p = _startLocalPos; p.y += Mathf.Sin(_t) * bobAmplitude;
        //transform.localPosition = p;

        //// billboard
        //if (billboardTarget)
        //{
        //    var fwd = (transform.position - billboardTarget.position).normalized;
        //    var targetRot = Quaternion.LookRotation(fwd, Vector3.up);
        //    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * faceCameraLerp);
        //}
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
            PlayAyat();
            PlayArti();
        }
    }

    public void Hide()
    {
        StopAll();
        gameObject.SetActive(false);
    }

    public void PlayAyat()
    {
        if (!_current || !ayatSource || !_current.ayatAudio) return;
        ayatSource.Stop(); ayatSource.clip = _current.ayatAudio; ayatSource.Play();
    }

    public void PlayArti()
    {
        if (!_current || !artiSource || !_current.artiAudio) return;
        artiSource.Stop(); artiSource.clip = _current.artiAudio; artiSource.Play();
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
