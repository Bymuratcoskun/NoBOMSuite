using Xunit;

namespace NoBOMSuite.Tests;

/// <summary>
/// `.bomconfig` süreç geneli TEK dosyadır (Environment.CurrentDirectory altında).
/// Ona yazan testler paralel koşarsa birbirinin yapılandırmasını okur — bu yarış
/// 2026-08-30'da canlı görüldü: çevrimdışı-kapısı testi, komşu testin yazdığı
/// `StrictOfflineMode=false` değerini okudu. Aynı koleksiyona konan sınıflar
/// xUnit tarafından paralel KOŞTURULMAZ.
/// </summary>
[CollectionDefinition("BomConfig")]
public class BomConfigKoleksiyonu { }
