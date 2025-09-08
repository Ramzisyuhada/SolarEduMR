using UnityEngine;

public class OrbitSlot : MonoBehaviour
{
    [Range(1, 32)]
    public int Index;              // 1..N dari Matahari
    public Transform SnapPoint;    // posisi target planet

    // Dipanggil saat reset ronde untuk bersihkan efek visual
    public void ClearHighlight()
    {
        // TODO: reset material/outline/partikel kalau ada
    }

    // Dipanggil ketika planet sukses ditempatkan di orbit ini
    public void BlinkFeedback()
    {
        // TODO: efek highlight singkat (ubah warna, partikel, dsb.)
    }

    /// <summary>
    /// Jika kamu ingin men-set orbit index secara eksplisit dari luar:
    /// </summary>
    public void AssignPlanet(Planets p)
    {
        if (!p) return;
        // Pastikan method ini hanya dipanggil di server (dalam game real)
        if (p.IsServer)
        {
            p.SetOrbitIndex(Index);
        }
    }
}
