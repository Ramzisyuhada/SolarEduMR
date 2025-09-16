using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class XRTouchResponder : MonoBehaviour
{
    public GameObject highlight;      // opsional: glow/visual saat disentuh
    public GameObject targetToToggle; // objek yang mau diaktif/nonaktifkan

    XRSimpleInteractable _interactable;

    void Awake()
    {
        _interactable = GetComponent<XRSimpleInteractable>();
        _interactable.hoverEntered.AddListener(OnHoverEnter);
        _interactable.hoverExited.AddListener(OnHoverExit);
        _interactable.selectEntered.AddListener(OnSelectEnter); // tekan/grab
    }

    void OnDestroy()
    {
        if (!_interactable) return;
        _interactable.hoverEntered.RemoveListener(OnHoverEnter);
        _interactable.hoverExited.RemoveListener(OnHoverExit);
        _interactable.selectEntered.RemoveListener(OnSelectEnter);
    }

    void OnHoverEnter(HoverEnterEventArgs _)
    {
        if (highlight) highlight.SetActive(true);
    }

    void OnHoverExit(HoverExitEventArgs _)
    {
        if (highlight) highlight.SetActive(false);
    }

    void OnSelectEnter(SelectEnterEventArgs _)
    {
        if (targetToToggle) targetToToggle.SetActive(!targetToToggle.activeSelf);
        Debug.Log("[XRTouchResponder] Disentuh/tekan → toggle");
    }
}
