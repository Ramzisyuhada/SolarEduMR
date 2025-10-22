// TeamScore.cs — tempel di 1 GameObject di scene (mis. Canvas kuis)
using Unity.Netcode;
using UnityEngine;

public class TeamScore : NetworkBehaviour
{
    public NetworkVariable<int> Correct = new(writePerm: NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Total = new(writePerm: NetworkVariableWritePermission.Server);

    public float Accuracy => Total.Value == 0 ? 0f : (Correct.Value * 100f / Total.Value);
}
