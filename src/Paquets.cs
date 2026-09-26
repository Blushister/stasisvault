using ProtoBuf;

namespace CurveoStockage;

[ProtoContract]
public class EntreeListe
{
    [ProtoMember(1)] public string Cle = "";
    [ProtoMember(2)] public byte[] Pile = Array.Empty<byte>();
    [ProtoMember(3)] public long Quantite;
    /// <summary>Toujours envoyé : protobuf omet les zéros et le 0 (figé) serait relu comme 1.</summary>
    [ProtoMember(4, IsRequired = true)] public float Taux = 1;
}

[ProtoContract]
public class PaquetListe
{
    [ProtoMember(1)] public List<EntreeListe> Entrees = new();
    [ProtoMember(2)] public long Objets;
    [ProtoMember(3)] public long ObjetsMax;
    [ProtoMember(4)] public int Types;
    [ProtoMember(5)] public int TypesMax;
    [ProtoMember(6)] public string? Erreur;
    [ProtoMember(7)] public int Mode;
    [ProtoMember(8)] public bool StabilisateurActif;
    /// <summary>Clés épinglées par le joueur qui reçoit la liste.</summary>
    [ProtoMember(9)] public List<string> Epingles = new();
    [ProtoMember(10)] public List<EvenementJournal> Journal = new();
    /// <summary>Le réseau a un atelier : la grille d'artisanat s'affiche.</summary>
    [ProtoMember(11)] public bool Atelier;
}

/// <summary>Mode : 0 = une pile dans la main, 1 = une demi-pile dans la main, 2 = une pile vers l'inventaire.</summary>
[ProtoContract]
public class PaquetExtraire
{
    [ProtoMember(1)] public string Cle = "";
    [ProtoMember(2)] public int Mode;

    /// <summary>Lit une demande envoyée par un client ; null si elle est illisible ou invalide.</summary>
    public static PaquetExtraire? Lire(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            var p = Vintagestory.API.Util.SerializerUtil.Deserialize<PaquetExtraire>(data);
            return p != null && p.Cle.Length > 0 && p.Cle.Length <= IdPaquets.CleMax && p.Mode is >= 0 and <= 2 ? p : null;
        }
        catch { return null; }
    }
}

public static class IdPaquets
{
    /// <summary>Longueur maximale d'une clé d'objet reçue d'un client (SHA1 en hexadécimal = 40).</summary>
    public const int CleMax = 64;

    public const int DemandeListe = 2000, Extraire = 2001, DeposerSouris = 2002, Epingler = 2003, ViderAtelier = 2004, RemplirAtelier = 2005;
    public const int Liste = 3000, Refus = 3001;
}
