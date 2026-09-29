using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CurveoStockage;

/// <summary>
/// Réglages du mod (ModConfig/stasisvault.json, en anglais, côté serveur, envoyés aux joueurs à la connexion).
/// Une section par partie du mod ; chaque partie peut être désactivée : sa recette disparaît et les blocs déjà
/// posés restent inertes (rien n'est détruit, le contenu est gardé).
/// </summary>
public class ConfigStockage
{
    [JsonProperty("_Readme", Order = -2)]
    public string Aide => "Stasis Vault settings. Each part can be disabled with Enabled: its recipe is removed and already placed blocks stay inert (nothing is destroyed). SpoilRate: 1 = normal speed, 0 = frozen. HoursPerCharge: in-game hours. Fuel: charges given by one fuel item. Restart the server after editing.";

    [JsonProperty("Network")]
    public ReglagesReseau Reseau { get; set; } = new();
    [JsonProperty("Cylinders")]
    public ReglagesCylindres Cylindres { get; set; } = new();
    [JsonProperty("Fuel")]
    public ReglagesCarburant Carburant { get; set; } = new();
    [JsonProperty("Stabilizer")]
    public ReglagesStabilisateur Stabilisateur { get; set; } = new();
    [JsonProperty("Anchor")]
    public ReglagesAncre Ancre { get; set; } = new();
    [JsonProperty("Wireless")]
    public ReglagesSansFil SansFil { get; set; } = new();
    [JsonProperty("Workshop")]
    public ReglagesAtelier Atelier { get; set; } = new();
    [JsonProperty("Buses")]
    public ReglagesBus Bus { get; set; } = new();
    [JsonProperty("Autocrafter")]
    public ReglagesAutomate Automate { get; set; } = new();

    /// <summary>Active /stockage autotest (monde de test uniquement : fait avancer le temps de 10 jours).</summary>
    [JsonProperty("AutoTestEnabled")]
    public bool AutoTestActif { get; set; } = false;

    /// <summary>Effet réel d'un matériau de cylindre : « refrigere », « stase » ou « normal ».</summary>
    public string Effet(string materiau) => materiau switch
    {
        "refrigere" when Cylindres.Actif("refrigere") => "refrigere",
        "stase" when Cylindres.Actif("stase") => "stase",
        _ => "normal",
    };

    /// <summary>Capacité d'un cylindre : celle de la config, sinon celle de son objet (attributs « capacite »).</summary>
    public (int objets, int types) Capacite(CollectibleObject cylindre)
    {
        var m = cylindre.Variant["materiau"];
        if (m != null && Cylindres.Materiaux.TryGetValue(m, out var c) && c.Objets > 0 && c.Types > 0) return (c.Objets, c.Types);
        var cap = cylindre.Attributes?["capacite"];
        return (cap?["objets"].AsInt(2000) ?? 2000, cap?["types"].AsInt(25) ?? 25);
    }

    /// <summary>Codes des objets dont la recette est retirée parce que leur partie du mod est désactivée.</summary>
    public List<string> RecettesDesactivees()
    {
        var codes = new List<string>();
        foreach (var (m, c) in Cylindres.Materiaux) if (!c.Actif) codes.Add("curveostockage:cylindre-" + m);
        if (!Stabilisateur.Actif) codes.Add("curveostockage:stabilisateur-north");
        if (!Ancre.Actif) codes.Add("curveostockage:ancre-north");
        if (!SansFil.Actif) codes.AddRange(new[] { "curveostockage:emetteur-north", "curveostockage:tablette" });
        if (!Atelier.Actif) codes.Add("curveostockage:atelier-north");
        if (!Bus.Import.Actif) codes.Add("curveostockage:busimport-north");
        if (!Bus.Export.Actif) codes.Add("curveostockage:busexport-north");
        if (!Bus.Stockage.Actif) codes.Add("curveostockage:busstockage-north");
        if (!Automate.Actif) codes.AddRange(new[] { "curveostockage:automate-north", "curveostockage:carte-vierge" });
        return codes;
    }

    public const string Fichier = "stasisvault.json", AncienFichier = "curveostockage.json";

    /// <summary>Convertit une config d'avant la 1.9 (curveostockage.json, réglages à plat, en français) en gardant ses valeurs.</summary>
    public static ConfigStockage Lire(JObject? brut)
    {
        var c = new ConfigStockage();
        if (brut == null) return c;
        double D(string k, double d) => brut[k]?.Value<double>() ?? d;
        int I(string k, int d) => brut[k]?.Value<int>() ?? d;
        bool B(string k, bool d) => brut[k]?.Value<bool>() ?? d;
        c.Cylindres.FacteurRefrigere = (float)D("FacteurRefrigere", 0.5);
        c.Cylindres.FacteurStaseRepos = c.Cylindres.FacteurRefrigere;
        c.Stabilisateur.ModeI.Facteur = (float)D("FacteurStabilisateurI", 0.5);
        c.Stabilisateur.ModeII.Facteur = (float)D("FacteurStabilisateurII", 0.1);
        c.Stabilisateur.ModeStase.Facteur = c.Stabilisateur.ModeII.Facteur;
        c.Stabilisateur.ModeI.HeuresParCharge = D("HeuresParEngrenageI", 216);
        c.Stabilisateur.ModeII.HeuresParCharge = D("HeuresParEngrenageII", 72);
        c.Stabilisateur.ModeStase.HeuresParCharge = D("HeuresParEngrenageStase", 24);
        c.Stabilisateur.MultiplicateurTempete = D("MultiplicateurTempete", 3);
        c.Ancre.MultiplicateurTempete = D("MultiplicateurTempete", 3);
        c.Stabilisateur.ChargeMax = c.Ancre.ChargeMax = I("EngrenagesMax", 16);
        c.Reseau.BlocsMax = I("BlocsMaxParReseau", 1024);
        c.Reseau.TemperatureMax = (float)D("TemperatureMax", 50);
        c.SansFil.Portee = D("PorteeEmetteur", 128);
        c.Ancre.HeuresParCharge = D("HeuresParEngrenageAncre", 48);
        c.Ancre.ColonnesMax = I("ColonnesMaxParAncre", 4);
        c.Ancre.AncresParJoueur = I("AncresParJoueur", 1);
        c.AutoTestActif = B("AutoTestActif", false);
        c.Cylindres.Materiaux["refrigere"].Actif = B("CylindreRefrigereActif", true);
        c.Cylindres.Materiaux["stase"].Actif = B("CylindreStaseActif", true);
        c.Stabilisateur.ModeStase.Actif = c.Cylindres.Materiaux["stase"].Actif;
        if (brut["Capacites"] is JObject caps)
            foreach (var (m, v) in caps)
                if (c.Cylindres.Materiaux.TryGetValue(m, out var r) && v is JObject o)
                { r.Objets = o["Objets"]?.Value<int>() ?? r.Objets; r.Types = o["Types"]?.Value<int>() ?? r.Types; }
        c.Bus.IntervalleMs = I("IntervalleBusMs", 1000);
        c.Bus.ObjetsParTransfert = I("ObjetsParTransfert", 16);
        c.Automate.IntervalleMs = I("IntervalleAutomateMs", 4000);
        c.Automate.BonusMecanique = (float)D("BonusMecanique", 3);
        return c;
    }
}

public class ReglagesReseau
{
    /// <summary>Taille maximale d'un réseau (blocs parcourus depuis le cœur).</summary>
    [JsonProperty("MaxBlocks")]
    public int BlocsMax { get; set; } = 1024;
    /// <summary>Au-delà de cette température (°C), un objet est refusé (lingots chauds…).</summary>
    [JsonProperty("MaxItemTemperature")]
    public float TemperatureMax { get; set; } = 50;
    /// <summary>Déplace peu à peu les objets vers le cylindre le plus adapté (denrées en stase, le reste ailleurs).</summary>
    [JsonProperty("AutoSort")]
    public bool TriAutomatique { get; set; } = true;
    [JsonProperty("AutoSortMovesPerPass")]
    public int DeplacementsParTri { get; set; } = 6;
    /// <summary>Journal d'activité : durée gardée (heures réelles) et nombre d'évènements.</summary>
    [JsonProperty("ActivityLogHours")]
    public double JournalHeures { get; set; } = 24;
    [JsonProperty("ActivityLogMaxEntries")]
    public int JournalEvenementsMax { get; set; } = 40;
    /// <summary>Fraîcheur (0 à 1) sous laquelle une denrée est signalée dans le journal ; 0 = jamais.</summary>
    [JsonProperty("RotAlertBelowFreshness")]
    public float AlerteFraicheur { get; set; } = 0.2f;
}

public class ReglageCylindre
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    [JsonProperty("MaxItems")]
    public int Objets { get; set; }
    [JsonProperty("MaxItemTypes")]
    public int Types { get; set; }
    public ReglageCylindre() { }
    public ReglageCylindre(int objets, int types) { Objets = objets; Types = types; }
}

public class ReglagesCylindres
{
    /// <summary>Par matériau : actif (recette), objets et types différents qu'il contient (s'applique aussi aux cylindres existants).</summary>
    [JsonIgnore]
    public Dictionary<string, ReglageCylindre> Materiaux { get; } = new()
    {
        ["cuivre"] = new(2000, 25), ["bronze"] = new(8000, 50), ["fer"] = new(16000, 75),
        ["acier"] = new(32000, 100), ["refrigere"] = new(4000, 30), ["stase"] = new(2000, 25),
    };
    /// <summary>Vitesse du pourrissement dans un cylindre réfrigéré (1 = normale).</summary>
    [JsonProperty("RefrigeratedSpoilRate")]
    public float FacteurRefrigere { get; set; } = 0.5f;
    /// <summary>Cylindre de stase hors stase (stabilisateur arrêté ou vide) : il réfrigère à cette vitesse.</summary>
    [JsonProperty("StasisIdleSpoilRate")]
    public float FacteurStaseRepos { get; set; } = 0.5f;
    /// <summary>Cylindre de stase avec le stabilisateur en mode stase (0 = figé).</summary>
    [JsonProperty("StasisFrozenSpoilRate")]
    public float FacteurStaseFige { get; set; } = 0f;

    public bool Actif(string materiau) => !Materiaux.TryGetValue(materiau, out var c) || c.Actif;

    /// <summary>Noms anglais des matériaux dans le fichier (les codes du mod restent en français).</summary>
    private static readonly Dictionary<string, string> Anglais = new()
    {
        ["cuivre"] = "copper", ["bronze"] = "bronze", ["fer"] = "iron", ["acier"] = "steel", ["refrigere"] = "refrigerated", ["stase"] = "stasis",
    };

    [JsonProperty("Materials", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<string, ReglageCylindre> MateriauxFichier
    {
        get => Materiaux.ToDictionary(kv => Anglais.GetValueOrDefault(kv.Key, kv.Key), kv => kv.Value);
        set
        {
            foreach (var (nom, reglage) in value ?? new())
            {
                var code = Anglais.FirstOrDefault(x => x.Value == nom).Key ?? nom;
                Materiaux[code] = reglage;
            }
        }
    }
}

public class ReglagesCarburant
{
    /// <summary>Objet brûlé par le stabilisateur et l'ancre.</summary>
    [JsonProperty("Item")]
    public string Objet { get; set; } = "game:gear-temporal";
    /// <summary>Charges apportées par un objet (la consommation se compte en charges).</summary>
    [JsonProperty("ChargePerItem")]
    public double ChargeParObjet { get; set; } = 1;
}

public class ReglageMode
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>Vitesse du pourrissement dans le réseau (1 = normale, 0 = figée).</summary>
    [JsonProperty("SpoilRate")]
    public float Facteur { get; set; }
    /// <summary>Heures de jeu couvertes par une charge.</summary>
    [JsonProperty("HoursPerCharge")]
    public double HeuresParCharge { get; set; }
    public ReglageMode() { }
    public ReglageMode(float facteur, double heures) { Facteur = facteur; HeuresParCharge = heures; }
}

public class ReglagesStabilisateur
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>false : les modes marchent sans carburant.</summary>
    [JsonProperty("ConsumesFuel")]
    public bool Consomme { get; set; } = true;
    [JsonProperty("MaxCharge")]
    public double ChargeMax { get; set; } = 16;
    [JsonProperty("ModeI")]
    public ReglageMode ModeI { get; set; } = new(0.5f, 216);
    [JsonProperty("ModeII")]
    public ReglageMode ModeII { get; set; } = new(0.1f, 72);
    /// <summary>Stase : fige les cylindres de stase (voir Cylindres.FacteurStaseFige), ralentit les autres de son facteur.</summary>
    [JsonProperty("ModeStasis")]
    public ReglageMode ModeStase { get; set; } = new(0.1f, 24);
    /// <summary>Pendant une tempête temporelle, la consommation est multipliée par ce nombre (1 = pas de différence).</summary>
    [JsonProperty("StormConsumptionMultiplier")]
    public double MultiplicateurTempete { get; set; } = 3;

    public ReglageMode? Mode(int n) => n switch { 1 => ModeI, 2 => ModeII, 3 => ModeStase, _ => null };
}

public class ReglagesAncre
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>false : les ancres gardent leurs chunks sans carburant.</summary>
    [JsonProperty("ConsumesFuel")]
    public bool Consomme { get; set; } = true;
    [JsonProperty("MaxCharge")]
    public double ChargeMax { get; set; } = 16;
    [JsonProperty("HoursPerCharge")]
    public double HeuresParCharge { get; set; } = 48;
    [JsonProperty("StormConsumptionMultiplier")]
    public double MultiplicateurTempete { get; set; } = 3;
    /// <summary>Colonnes de chunks gardées par une ancre (les plus proches d'elle).</summary>
    [JsonProperty("MaxChunkColumns")]
    public int ColonnesMax { get; set; } = 4;
    [JsonProperty("MaxAnchorsPerPlayer")]
    public int AncresParJoueur { get; set; } = 1;
}

public class ReglagesSansFil
{
    /// <summary>Tablette et émetteur.</summary>
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>Portée d'un émetteur en blocs ; 0 = illimitée (même dimension).</summary>
    [JsonProperty("RangeBlocks")]
    public double Portee { get; set; } = 128;
}

public class ReglagesAtelier
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>Après chaque fabrication, les cases vidées sont re-remplies depuis le réseau.</summary>
    [JsonProperty("AutoRefillGrid")]
    public bool RemplissageAuto { get; set; } = true;
}

public class ReglageActif
{
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
}

public class ReglagesBus
{
    [JsonProperty("ImportBus")]
    public ReglageActif Import { get; set; } = new();
    [JsonProperty("ExportBus")]
    public ReglageActif Export { get; set; } = new();
    [JsonProperty("StorageBus")]
    public ReglageActif Stockage { get; set; } = new();
    /// <summary>Intervalle entre deux transferts d'un bus d'import ou d'export (millisecondes).</summary>
    [JsonProperty("TransferIntervalMs")]
    public int IntervalleMs { get; set; } = 1000;
    /// <summary>Objets déplacés au plus à chaque transfert.</summary>
    [JsonProperty("ItemsPerTransfer")]
    public int ObjetsParTransfert { get; set; } = 16;
    /// <summary>Un bus n'agit que sur les conteneurs accessibles à celui qui l'a posé (zones protégées, verrous).</summary>
    [JsonProperty("RespectClaimsAndLocks")]
    public bool RespecterProtections { get; set; } = true;
}

public class ReglagesAutomate
{
    /// <summary>Automate horloger et cartes perforées.</summary>
    [JsonProperty("Enabled")]
    public bool Actif { get; set; } = true;
    /// <summary>Durée d'une fabrication sans axe (millisecondes).</summary>
    [JsonProperty("CraftIntervalMs")]
    public int IntervalleMs { get; set; } = 4000;
    /// <summary>Un axe mécanique accélère l'automate.</summary>
    [JsonProperty("AxleSpeedup")]
    public bool AxeActif { get; set; } = true;
    /// <summary>Accélération à pleine vitesse d'axe (×1 + ce bonus).</summary>
    [JsonProperty("AxleSpeedBonus")]
    public float BonusMecanique { get; set; } = 3f;
    /// <summary>Vitesse d'axe qui donne le bonus complet.</summary>
    [JsonProperty("AxleSpeedForFullBonus")]
    public float VitesseAxePleine { get; set; } = 1f;
    /// <summary>Niveaux d'ingrédients fabriqués à la chaîne pour une commande.</summary>
    [JsonProperty("MaxSubCraftDepth")]
    public int ProfondeurMax { get; set; } = 4;
    /// <summary>Commandes en attente au plus par réseau.</summary>
    [JsonProperty("MaxQueuedOrders")]
    public int CommandesMax { get; set; } = 50;
}

/// <summary>
/// Config envoyée par le serveur à chaque joueur à la connexion, pour que le client affiche les mêmes valeurs
/// (autonomie, limites). En JSON : protobuf omettrait les zéros, relus comme valeurs par défaut.
/// </summary>
[ProtoBuf.ProtoContract]
public class MsgConfig
{
    [ProtoBuf.ProtoMember(1)] public string Json = "";

    public static MsgConfig De(ConfigStockage c) => new() { Json = JsonConvert.SerializeObject(c) };

    public ConfigStockage? Lire()
    {
        try { return JsonConvert.DeserializeObject<ConfigStockage>(Json); } catch { return null; }
    }
}
