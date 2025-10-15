using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class M_Soal 
{
    public int idx;
    public string soal;    
    public int kunci;
    public bool isEnd = false;
    public List<string> pilihanJawabans = new List<string>();
}