using UnityEngine;

[CreateAssetMenu(fileName = "QuranData", menuName = "Custom/QuranData")]
public class QuranData : ScriptableObject
{
    [Header("Info Ayat")]
    public Sprite ayatJudul;
    public Sprite ayatSprite;          // Sprite untuk ayat
    public Sprite artiSprite;          // Sprite untuk arti

    [Header("Audio Ayat")]
    public AudioClip ayatAudio;
    public AudioClip artiAudio;
}
