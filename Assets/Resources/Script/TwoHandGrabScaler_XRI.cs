// TwoHandGrabScaler_XRI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable))]
public class TwoHandGrabScaler_XRI : MonoBehaviour
{
    [Header("Limits & Feel")]
    [Min(0.01f)] public float minScale = 0.1f;
    [Min(0.01f)] public float maxScale = 5f;
    [Range(0f, 1f)] public float smooth = 0.15f; // 0 = no smoothing

    [Tooltip("Jika true, scaling hanya aktif saat persis 2 interactor memegang. Kalau false, 2+ juga boleh (ambil 2 pertama).")]
    public bool requireExactlyTwo = true;

    [Header("Optional")]
    public bool preserveUniformScale = true; // jaga skala seragam XYZ

    UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grab;
    readonly List<UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor> _interactors = new();
    float _initialDistance;
    Vector3 _initialScale;
    Vector3 _targetScale;
    bool _isScaling;

    void Awake()
    {
        _grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        _grab.selectEntered.AddListener(OnSelectEntered);
        _grab.selectExited.AddListener(OnSelectExited);
        _targetScale = transform.localScale;
    }

    void OnDestroy()
    {
        if (_grab)
        {
            _grab.selectEntered.RemoveListener(OnSelectEntered);
            _grab.selectExited.RemoveListener(OnSelectExited);
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!_interactors.Contains(args.interactorObject))
            _interactors.Add(args.interactorObject);
        TryBeginScale();
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        _interactors.Remove(args.interactorObject);
        TryEndScale();
    }

    void TryBeginScale()
    {
        if (_interactors.Count < 2) return;
        if (requireExactlyTwo && _interactors.Count != 2) return;

        var a = _interactors[0].transform.position;
        var b = _interactors[1].transform.position;

        _initialDistance = Vector3.Distance(a, b);
        if (_initialDistance < 0.0001f) return;

        _initialScale = transform.localScale;
        _isScaling = true;
    }

    void TryEndScale()
    {
        if (_interactors.Count < 2) _isScaling = false;
    }

    void Update()
    {
        if (!_isScaling) return;
        if (_interactors.Count < 2) { _isScaling = false; return; }

        var a = _interactors[0].transform.position;
        var b = _interactors[1].transform.position;

        var currentDistance = Vector3.Distance(a, b);
        if (_initialDistance < 0.0001f) return;

        float ratio = currentDistance / _initialDistance;

        Vector3 wanted = preserveUniformScale
            ? Vector3.one * Mathf.Clamp(_initialScale.x * ratio, minScale, maxScale)
            : new Vector3(
                Mathf.Clamp(_initialScale.x * ratio, minScale, maxScale),
                Mathf.Clamp(_initialScale.y * ratio, minScale, maxScale),
                Mathf.Clamp(_initialScale.z * ratio, minScale, maxScale)
              );

        // smoothing optional
        _targetScale = wanted;
        transform.localScale = (smooth <= 0f)
            ? _targetScale
            : Vector3.Lerp(transform.localScale, _targetScale, 1f - Mathf.Pow(1f - 0.5f, Time.deltaTime / Mathf.Max(0.0001f, smooth)));
    }
}
