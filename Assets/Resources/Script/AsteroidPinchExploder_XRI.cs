using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class AsteroidPinchExploder_XRI : MonoBehaviour
{
    public NetworkAsteroid asteroid;   // drag komponen NetworkAsteroid di sini
    UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable _it;

    void Awake()
    {
        _it = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        // Saat jari melakukan "select" (pinch) pada asteroid → meledak
        _it.selectEntered.AddListener(OnPinchSelect);
    }

    void OnDestroy()
    {
        if (_it)
        {
            _it.selectEntered.RemoveListener(OnPinchSelect);
        }
    }

    void OnPinchSelect(SelectEnterEventArgs args)
    {
        if (!asteroid) return;

        // Cari titik terdekat dari interactor ke asteroid untuk efek ledak yang masuk akal
        Transform interactorTf = args.interactorObject.GetAttachTransform(_it);
        Vector3 from = interactorTf ? interactorTf.position : transform.position;
        Vector3 hitPoint = GetClosestPointOnAsteroid(from);

        asteroid.ClientRequestExplode(hitPoint);
    }

    Vector3 GetClosestPointOnAsteroid(Vector3 from)
    {
        // Kalau ada collider, ambil titik terdekat; kalau tidak, pakai posisi asteroid.
        var col = GetComponent<Collider>();
        if (col != null) return col.ClosestPoint(from);
        return transform.position;
    }
}
