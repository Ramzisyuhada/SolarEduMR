using UnityEngine;

[CreateAssetMenu(menuName = "SolarSystem/Planet Data (Single Image)", fileName = "PlanetData")]
public class PlanetData : ScriptableObject
{
    [Header("Gambar berisi Nama + Deskripsi Planet")]
    public Sprite infoImage;        // 1 gambar berisi nama + deskripsi

    [Header("Opsional")]
    public Sprite planetPicture;    // gambar planet terpisah (opsional)
    public AudioClip narration;     // audio narasi (opsional)
}
