using UnityEngine;

public class AsteroidFxRegistry : MonoBehaviour
{
    public GameObject[] prefabs; // nama prefab harus unik
    static AsteroidFxRegistry _inst;

    void Awake() { _inst = this; }

    public static GameObject Get(string name)
    {
        Debug.Log(name);

        if (_inst == null || _inst.prefabs == null || string.IsNullOrEmpty(name)) return null;

        foreach (var p in _inst.prefabs) if (p && p.name == name) return p;
        // Fallback: coba Resources.Load dengan nama yang sama
        var res = Resources.Load<GameObject>(name);
        return res;
    }
}