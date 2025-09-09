using UnityEngine;
public class OrbitTriggerForwarder : MonoBehaviour
{
    public OrbitSlot parentSlot;
    void OnTriggerEnter(Collider other) { if (parentSlot) parentSlot.OnChildTriggerEnter(other); }
    void OnTriggerExit(Collider other) { if (parentSlot) parentSlot.OnChildTriggerExit(other); }
}
