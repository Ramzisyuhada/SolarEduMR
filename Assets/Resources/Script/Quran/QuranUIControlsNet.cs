using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
#if TMP_PRESENT
using TMPro;
#endif

public class QuranUIControlsNet : NetworkBehaviour
{
    [Header("Target (Networked)")]
    public NetworkObject carouselNetworkObject;
    private QuranCarousel3DUI_Net _carousel;

    [Header("Buttons")]
    public Button btnPrev;
    public Button btnNext;
    public Button btnPlayStop;   // toggle Play/Stop (opsional)
    public Button btnStop;       // tombol Stop khusus (baru)

    [Header("Optional Labels")]
    public Text btnPlayStopLabel;     // untuk label dynamic Play/Stop
#if TMP_PRESENT
    public TMP_Text btnPlayStopTMP;
#endif

    [Header("UX")]
    public bool disableUntilReady = true;
    public float labelRefreshSeconds = 0.5f;

    public override void OnNetworkSpawn()
    {
        if (!_carousel && carouselNetworkObject)
            _carousel = carouselNetworkObject.GetComponent<QuranCarousel3DUI_Net>();
        if (!_carousel)
            _carousel = FindObjectOfType<QuranCarousel3DUI_Net>(true);

        WireButtons();

        if (labelRefreshSeconds > 0f)
            InvokeRepeating(nameof(RefreshPlayStopLabel), 0.2f, Mathf.Max(0.2f, labelRefreshSeconds));

        RefreshPlayStopLabel();
        SetInteractable(IsReady());
    }

    void OnDisable()
    {
        UnwireButtons();
    }

    // ===== Wiring =====
    void WireButtons()
    {
        UnwireButtons();

        if (btnPrev)
            btnPrev.onClick.AddListener(() =>
            {
                if (!IsReady()) return;
                _carousel.OnPrev();      // di dalamnya sudah ServerRpc
                RefreshPlayStopLabel();
            });

        if (btnNext)
            btnNext.onClick.AddListener(() =>
            {
                if (!IsReady()) return;
                _carousel.OnNext();
                RefreshPlayStopLabel();
            });

        if (btnPlayStop)
            btnPlayStop.onClick.AddListener(() =>
            {
                if (!IsReady()) return;
                _carousel.OnPlayStopToggle();   // toggle play/stop via ServerRpc
                RefreshPlayStopLabel();
            });

        if (btnStop)
            btnStop.onClick.AddListener(() =>
            {
                if (!IsReady()) return;
                // panggil stop khusus (broadcast ke semua klien via ServerRpc)
                _carousel.OnStopAudio();
                RefreshPlayStopLabel();
            });
    }

    void UnwireButtons()
    {
        if (btnPrev) btnPrev.onClick.RemoveAllListeners();
        if (btnNext) btnNext.onClick.RemoveAllListeners();
        if (btnPlayStop) btnPlayStop.onClick.RemoveAllListeners();
        if (btnStop) btnStop.onClick.RemoveAllListeners();
    }

    // ===== Helpers =====
    bool IsReady()
    {
        if (_carousel == null) return false;
        // pastikan object target sudah Spawned agar ServerRpc berfungsi
        if (!_carousel.IsSpawned) return false;
        return true;
    }

    void SetInteractable(bool enabled)
    {
        if (!disableUntilReady) return;
        if (btnPrev) btnPrev.interactable = enabled;
        if (btnNext) btnNext.interactable = enabled;
        if (btnPlayStop) btnPlayStop.interactable = enabled;
        if (btnStop) btnStop.interactable = enabled;
    }

    void RefreshPlayStopLabel()
    {
        // jika kamu pakai tombol toggle, labelnya ikut status
        string label = "Play";
        if (_carousel && (_carousel.IsPlaying() || _carousel.IsAudioSourcePlaying()))
            label = "Stop";

        if (btnPlayStopLabel) btnPlayStopLabel.text = label;
#if TMP_PRESENT
        if (btnPlayStopTMP) btnPlayStopTMP.text = label;
#endif
    }
}
