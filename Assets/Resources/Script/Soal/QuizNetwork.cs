// QuizNetwork.cs
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class QuizNetwork : NetworkBehaviour
{
    public V_Soal view;
    public TeamScore team;      // drag TeamScore dari scene
    public float nextDelay = 1.5f;

    private VM_Soal vm;
    private List<int> order;
    private int orderIdx = -1;

    public NetworkVariable<int> CurrentQuestionIndex =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> Locked =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // opsional: untuk sinkron tampilan “jawaban terakhir”
    public NetworkVariable<int> LastChosen =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> LastRight =
        new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    void Start()
    {
        CurrentQuestionIndex.OnValueChanged += (_, __) => view?.RenderQuestion(CurrentQuestionIndex.Value);
        LastChosen.OnValueChanged += (_, __) => TryRenderLastFeedback();
        LastRight.OnValueChanged += (_, __) => TryRenderLastFeedback();
    }

    void TryRenderLastFeedback()
    {
        if (Locked.Value && LastRight.Value >= 0 && LastChosen.Value >= 0)
            view?.ShowFeedback(LastChosen.Value == LastRight.Value, LastChosen.Value, LastRight.Value);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            int opsi = view != null ? view.GetOptionCount() : 4;
            vm = new VM_Soal(opsi);

            order = new List<int>();
            for (int i = 0; i < vm.CountSoal(); i++) order.Add(i);
            Shuffle(order);

            if (team != null)
            {            // ⬅️ tambahan guard
                team.Correct.Value = 0;
                team.Total.Value = 0;
            }
            else
            {
                Debug.LogWarning("TeamScore belum di-assign ke QuizNetwork.");
            }

            NextQuestion();                 // ⬅️ dipanggil hanya di server
        }
    }

    // [Server]  ⬅️ HAPUS atribut ini
    void NextQuestion()
    {
        if (!IsServer) return;             // ⬅️ pengaman ekstra

        orderIdx++;
        if (orderIdx >= order.Count)
        {
            CurrentQuestionIndex.Value = -1; // selesai
            return;
        }
        Locked.Value = false;
        LastChosen.Value = -1;
        LastRight.Value = -1;
        CurrentQuestionIndex.Value = order[orderIdx];
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitAnswerServerRpc(int chosenIndex, ServerRpcParams _ = default)
    {
        if (Locked.Value || CurrentQuestionIndex.Value < 0) return;

        var soal = vm.GetSoalByIndex(CurrentQuestionIndex.Value);
        if (soal == null) return;

        bool correct = chosenIndex == soal.kunci;

        // ++ skor tim (sama untuk semua)
        if (team != null)
        {                // ⬅️ guard NRE
            team.Total.Value += 1;
            if (correct) team.Correct.Value += 1;
        }

        // kunci & simpan jawaban untuk sinkron UI
        Locked.Value = true;
        LastChosen.Value = chosenIndex;
        LastRight.Value = soal.kunci;

        ShowResultClientRpc(correct, chosenIndex, soal.kunci);
        StartCoroutine(WaitAndNext());
    }

    [ClientRpc]
    void ShowResultClientRpc(bool correct, int chosenIndex, int rightIndex)
    {
        view?.ShowFeedback(correct, chosenIndex, rightIndex);
    }

    IEnumerator WaitAndNext()
    {
        yield return new WaitForSeconds(nextDelay);
        NextQuestion();                    // ⬅️ aman karena NextQuestion cek IsServer
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
