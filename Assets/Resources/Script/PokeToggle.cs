using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(XRSimpleInteractable))]
public class PokeToggle : MonoBehaviour
{
    [Header("Targets")]
    public GameObject target;      // objek yang mau di-ON/OFF
    public GameObject highlight;   // opsional: efek glow / outline

    [Header("Debounce")]
    [Tooltip("Jeda minimal antar toggle (detik)")]
    public float toggleCooldown = 0.15f;

    XRSimpleInteractable _it;
    float _lastToggleTime;

    void Awake()
    {
        _it = GetComponent<XRSimpleInteractable>();

        // hover visual
        _it.hoverEntered.AddListener(_ => { if (highlight) highlight.SetActive(true); });
        _it.hoverExited.AddListener(_ => { if (highlight) highlight.SetActive(false); });

        // poke select
        _it.selectEntered.AddListener(args =>
        {
            if (Time.time - _lastToggleTime < toggleCooldown) return;
            _lastToggleTime = Time.time;

            if (target)
            {
                target.SetActive(!target.activeSelf);
                Debug.Log($"[PokeToggle] Telunjuk menekan → toggle: {target.activeSelf}");
            }

            // (Opsional) Haptics kalau interaktornya controller
            if (args.interactorObject is UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor ctrl)
            {
                // amplitudo, durasi
                ctrl.SendHapticImpulse(0.5f, 0.05f);
            }
        });
    }

    void OnDestroy()
    {
        if (!_it) return;
        _it.hoverEntered.RemoveAllListeners();
        _it.hoverExited.RemoveAllListeners();
        _it.selectEntered.RemoveAllListeners();
    }
}
