using UnityEngine;

public class ControlPanelUI : MonoBehaviour
{
    public SolarGameManager manager;

    public void OnClickStart()
    {
        if (manager) manager.StartRoundServerRpc();
    }

    public void OnClickVerify()
    {
        if (manager) manager.ForceVerifyServerRpc();
    }
}
