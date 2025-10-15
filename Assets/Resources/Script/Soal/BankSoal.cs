using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BankSoal
{
    // format penulisannya -(string)--> 
    // (soal)*(pilihanjawaban1)*(pilihanjawaban2)*(pilihanjawaban3)*.....*(pilihanjawaban-n)*(index jawaban benar)
    // pisahkan tiap soal dengan #

    public static string banksoaljawaban =
   "Planet terbesar di tata surya adalah….*Bumi*Yupiter*Saturnus*Uranus*2#" +
   "Planet yang dikenal sebagai planet merah adalah….*Venus*Mars*Merkurius*Neptunus*2#" +
   "Urutan planet yang benar dari yang terdekat dengan Matahari adalah….*Merkurius, Venus, Bumi, Mars*Venus, Merkurius, Bumi, Mars*Mars, Bumi, Venus, Merkurius*Merkurius, Bumi, Venus, Mars*1#" +
   "Planet yang memiliki cincin paling mencolok adalah….*Neptunus*Saturnus*Uranus*Yupiter*2#" +
   "Planet yang memiliki waktu rotasi paling cepat adalah….*Merkurius*Bumi*Yupiter*Mars*3#" +
   "Benda langit yang memantulkan cahaya matahari dan mengorbit planet disebut….*Satelit alami*Asteroid*Komet*Meteoroid*1#" +
   "Pusat tata surya adalah….*Bumi*Matahari*Bulan*Venus*2#" +
   "Planet yang paling dekat dengan Matahari adalah….*Venus*Bumi*Merkurius*Mars*3#" +
   "Planet yang disebut kembaran Bumi karena ukurannya mirip adalah….*Mars*Venus*Saturnus*Neptunus*2#" +
   "Gerakan planet mengelilingi Matahari disebut….*Rotasi*Revolusi*Evolusi*Translasi*2#" +
   "Satelit alami yang dimiliki Bumi adalah….*Titan*Europa*Bulan*Io*3#" +
   "Lapisan gas yang menyelimuti planet disebut….*Awan*Atmosfer*Eksosfer*Troposfer*2#" +
   "Planet yang memiliki suhu terpanas di tata surya adalah….*Merkurius*Venus*Mars*Yupiter*2#" +
   "Benda langit yang terbentuk dari batuan kecil yang mengorbit Matahari disebut….*Komet*Meteorit*Asteroid*Satelit*3#" +
   "Ekor komet selalu mengarah ke….*Matahari*Belakang arah gerak*Mengikuti orbit planet*Menjauh dari Matahari*4#" +
   "Planet yang berputar dengan arah berlawanan dari planet lainnya adalah….*Bumi*Venus*Mars*Saturnus*2#" +
   "Bintang terdekat dari Bumi adalah….*Alpha Centauri*Matahari*Betelgeuse*Sirius*2#" +
   "Planet yang dikenal memiliki warna biru karena gas metana adalah….*Saturnus*Uranus*Neptunus*Yupiter*3#" + // ← diperbaiki
   "Gerhana matahari terjadi ketika….*Bulan berada di antara Bumi dan Matahari*Bumi berada di antara Matahari dan Bulan*Matahari berada di antara Bumi dan Bulan*Bulan sejajar dengan Matahari tapi tidak menutupi*1#" +
   "Gerhana bulan terjadi ketika….*Bumi berada di antara Matahari dan Bulan*Bulan berada di antara Bumi dan Matahari*Matahari berada di antara Bumi dan Bulan*Bulan menjauh dari orbitnya*1#"; // (opsional) perbaiki agar kunci konsisten

}
