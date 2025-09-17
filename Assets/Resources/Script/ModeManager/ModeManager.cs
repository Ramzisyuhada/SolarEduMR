using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public enum AppMode { None, Planet, Quran, Arrange }

public class ModeManager : NetworkBehaviour
{
    public GameObject Game;
    // ---------------- Lifecycle ----------------
    public override void OnNetworkSpawn()
    {
        Game.SetActive(true);
    }

 

    // ---------------- API Umum ----------------

 

   
}
