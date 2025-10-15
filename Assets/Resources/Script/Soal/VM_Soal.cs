using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VM_Soal
{
    private readonly List<M_Soal> currSoal = new List<M_Soal>();
    public List<M_Soal> CurrSoal => currSoal;

    public int CurrIndex { get; private set; } = 0;

    public VM_Soal(int _ignoredOpsi) // opsi diabaikan; parser fleksibel
    {
        currSoal.AddRange(ParsingSoalJawabanInternal());
    }

    public int CountSoal() => currSoal.Count;

    public M_Soal GetSoalByIndex(int index)
    {
        if (index < 0 || index >= currSoal.Count) return null;
        return currSoal[index];
    }

    public void MarkSoalSelesai(int index, bool selesai = true)
    {
        var s = GetSoalByIndex(index);
        if (s != null) s.isEnd = selesai;
    }

    public bool CekSoalSudahSemua() => currSoal.All(x => x.isEnd);

    public static bool IsMenjawab(int idPilih0Based, int idKunci0Based) => idPilih0Based == idKunci0Based;

    private List<M_Soal> ParsingSoalJawabanInternal()
    {
        var list = new List<M_Soal>();
        string bankSoal = BankSoal.banksoaljawaban;
        if (string.IsNullOrEmpty(bankSoal))
        {
            Debug.LogError("Bank soal kosong.");
            return list;
        }

        var rows = bankSoal.Split('#');
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row)) continue;

            var t = row.Split('*');
            if (t.Length < 3)
            {
                Debug.LogError($"Format terlalu pendek: {row}");
                continue;
            }

            string teksSoal = t[0];
            if (!int.TryParse(t[t.Length - 1], out int kunciOneBased))
            {
                Debug.LogError($"Index jawaban tidak valid: {t[t.Length - 1]} | Soal: {teksSoal}");
                continue;
            }

            int choicesCount = t.Length - 2; // semua token antara soal & kunci = pilihan
            if (choicesCount <= 0)
            {
                Debug.LogError($"Tidak ada pilihan untuk soal: {teksSoal}");
                continue;
            }

            var pilihan = new List<string>(choicesCount);
            for (int i = 0; i < choicesCount; i++)
                pilihan.Add(t[1 + i]);

            int kunciZeroBased = kunciOneBased - 1;
            if (kunciZeroBased < 0 || kunciZeroBased >= choicesCount)
            {
                Debug.LogError($"Index kunci {kunciOneBased} di luar batas 1..{choicesCount} | Soal: {teksSoal}");
                continue;
            }

            list.Add(new M_Soal
            {
                idx = list.Count,
                soal = teksSoal,
                kunci = kunciZeroBased,
                pilihanJawabans = pilihan,
                isEnd = false
            });
        }

        return list;
    }
}
