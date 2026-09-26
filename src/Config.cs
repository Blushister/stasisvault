namespace CurveoStockage;

/// <summary>Réglages du stockage temporel (ModConfig/curveostockage.json, côté serveur).</summary>
public class ConfigStockage
{
    /// <summary>Vitesse du pourrissement dans un cylindre réfrigéré (1 = normale).</summary>
    public float FacteurRefrigere { get; set; } = 0.5f;

    /// <summary>Vitesse du pourrissement selon le mode du stabilisateur (1 = normale, 0 = figée).</summary>
    public float FacteurStabilisateurI { get; set; } = 0.5f;
    public float FacteurStabilisateurII { get; set; } = 0.1f;

    /// <summary>Heures de jeu couvertes par un engrenage temporel selon le mode.</summary>
    public double HeuresParEngrenageI { get; set; } = 216;
    public double HeuresParEngrenageII { get; set; } = 72;
    public double HeuresParEngrenageStase { get; set; } = 24;

    /// <summary>Pendant une tempête temporelle, le stabilisateur consomme autant de fois plus.</summary>
    public double MultiplicateurTempete { get; set; } = 3;

    /// <summary>Engrenages temporels qu'un stabilisateur peut contenir.</summary>
    public int EngrenagesMax { get; set; } = 16;

    /// <summary>Taille maximale d'un réseau (nombre de blocs parcourus depuis le cœur).</summary>
    public int BlocsMaxParReseau { get; set; } = 1024;

    /// <summary>Au-delà de cette température (°C), un objet est refusé (lingots chauds, etc.).</summary>
    public float TemperatureMax { get; set; } = 50;

    /// <summary>Portée (en blocs) d'un émetteur temporel pour la tablette.</summary>
    public double PorteeEmetteur { get; set; } = 128;

    /// <summary>Heures de jeu couvertes par un engrenage temporel dans une ancre.</summary>
    public double HeuresParEngrenageAncre { get; set; } = 48;

    /// <summary>Colonnes de chunks qu'une ancre peut garder chargées (les plus proches d'elle).</summary>
    public int ColonnesMaxParAncre { get; set; } = 4;

    /// <summary>Ancres actives par joueur.</summary>
    public int AncresParJoueur { get; set; } = 1;

    /// <summary>Active /stockage autotest (monde de test uniquement : fait avancer le temps de 10 jours).</summary>
    public bool AutoTestActif { get; set; } = false;
}

/// <summary>
/// Config envoyée par le serveur à chaque joueur à la connexion, pour que le client affiche les mêmes valeurs
/// (autonomie, limites). En JSON : protobuf omettrait les zéros, relus comme valeurs par défaut.
/// </summary>
[ProtoBuf.ProtoContract]
public class MsgConfig
{
    [ProtoBuf.ProtoMember(1)] public string Json = "";

    public static MsgConfig De(ConfigStockage c) => new() { Json = System.Text.Json.JsonSerializer.Serialize(c) };

    public ConfigStockage? Lire()
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<ConfigStockage>(Json); } catch { return null; }
    }
}
