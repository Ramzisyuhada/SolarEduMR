using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class V_Soal : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI tampilSoal;
    [SerializeField] private TextMeshProUGUI akurasiTMP;
    [SerializeField] private GameObject parentPilihanJawaban;
    public GameObject gambar19;
    public GameObject gambar20;

    [Header("Refs")]
    public QuizNetwork quiz;   // drag dari scene
    public TeamScore team;     // drag dari scene

    private VM_Soal vm;
    private List<Button> buttons = new();
    private List<TextMeshProUGUI> labels = new();

    void Awake()
    {
        // Cache tombol & label
        foreach (Transform t in parentPilihanJawaban.transform)
        {
            var btn = t.GetComponent<Button>();
            var txt = t.GetComponentInChildren<TextMeshProUGUI>();
            if (btn) buttons.Add(btn);
            if (txt) labels.Add(txt);
        }
    }

    void Start()
    {
        vm = new VM_Soal(buttons.Count); // parser fleksibel; angka diabaikan

        // Pasang listener tombol sekali
        for (int i = 0; i < buttons.Count; i++)
        {
            int localIndex = i;
            buttons[i].onClick.RemoveAllListeners();
            buttons[i].onClick.AddListener(() =>
            {
                if (quiz != null)
                    quiz.SubmitAnswerServerRpc(localIndex);
            });
        }

        // Akurasi tim realtime
        if (team != null)
        {
            team.Correct.OnValueChanged += (_, __) => RefreshAccuracy();
            team.Total.OnValueChanged += (_, __) => RefreshAccuracy();
            RefreshAccuracy();
        }
    }

    public int GetOptionCount() => buttons.Count;

    public void RenderQuestion(int soalIndex)
    {
        if (soalIndex < 0)
        {
            tampilSoal.text = "Kuis selesai. 🎉";
            SetButtonsInteractable(false);
            gambar19?.SetActive(false);
            gambar20?.SetActive(false);
            return;
        }

        var s = vm.GetSoalByIndex(soalIndex);
        if (s == null) return;

        tampilSoal.text = s.soal;

        // Atur teks & visibilitas tombol
        int opsi = s.pilihanJawabans.Count;
        for (int i = 0; i < buttons.Count; i++)
        {
            bool active = i < opsi;
            buttons[i].gameObject.SetActive(active);
            if (active)
            {
                var img = buttons[i].GetComponent<Image>();
                if (img) img.color = Color.white;

                if (i < labels.Count && labels[i] != null)
                    labels[i].text = "\t" + s.pilihanJawabans[i];
            }
        }

        SetButtonsInteractable(true);

        // Gambar khusus index
        if (soalIndex == 19) { if (gambar19) gambar19.SetActive(true); if (gambar20) gambar20.SetActive(false); }
        else if (soalIndex == 20) { if (gambar19) gambar19.SetActive(false); if (gambar20) gambar20.SetActive(true); }
        else { if (gambar19) gambar19.SetActive(false); if (gambar20) gambar20.SetActive(false); }
    }

    public void ShowFeedback(bool correct, int chosenIndex, int rightIndex)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (!buttons[i].gameObject.activeSelf) continue;
            var img = buttons[i].GetComponent<Image>();
            if (!img) continue;

            if (i == rightIndex) img.color = Color.green;
            else if (i == chosenIndex && !correct) img.color = Color.red;
            else img.color = Color.white;
        }

        SetButtonsInteractable(false);
        RefreshAccuracy();
    }

    void SetButtonsInteractable(bool v)
    {
        foreach (var b in buttons)
            if (b) b.interactable = v && b.gameObject.activeSelf;
    }

    void RefreshAccuracy()
    {
        if (!akurasiTMP || team == null) return;
        float acc = team.Total.Value == 0 ? 0f : (team.Correct.Value * 100f / team.Total.Value);
        akurasiTMP.text = acc.ToString("#.##") + "%";
    }

    // opsional tombol tutup
    public void close()
    {
        var obj = GameObject.Find("Pertanyaan(Clone)");
        if (obj) Destroy(obj);
    }
}
