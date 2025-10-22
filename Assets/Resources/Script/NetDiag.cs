using Unity.Netcode;
using UnityEngine;
using System.Security.Cryptography;
using System.Text;

public class NetDiagHashes : MonoBehaviour
{
    [ContextMenu("Dump Prefab Hashes (Manual Hash)")]
    void DumpPrefabHashes()
    {
        var cfg = NetworkManager.Singleton.NetworkConfig.Prefabs;
        Debug.Log($"[NGO] Prefabs count = {cfg.Prefabs.Count}");
        foreach (var np in cfg.Prefabs)
        {
            if (np?.Prefab == null)
            {
                Debug.Log(" - <null prefab>");
                continue;
            }

            uint hash = ManualHash(np.Prefab.name);
            Debug.Log($" - {np.Prefab.name}   hash={hash}");
        }
    }

    [ContextMenu("Dump Scene Hashes (Manual Hash)")]
    void DumpSceneHashes()
    {
        var nos = FindObjectsOfType<NetworkObject>(true);
        Debug.Log($"[NGO] Scene NetworkObjects = {nos.Length}");
        foreach (var no in nos)
        {
            uint hash = ManualHash(no.name);
            Debug.Log($" - {no.name}   IsSceneObject={no.IsSceneObject}   hash={hash}");
        }
    }

    uint ManualHash(string name)
    {
        // Hash sederhana dari nama prefab (mirip metode internal NGO)
        if (string.IsNullOrEmpty(name)) return 0;
        var bytes = Encoding.UTF8.GetBytes(name);
        using (var md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(bytes);
            return System.BitConverter.ToUInt32(hashBytes, 0);
        }
    }
}
