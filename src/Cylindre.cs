using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CurveoStockage;

/// <summary>Une pile rangée dans un cylindre. La quantité est celle de la pile (peut dépasser la taille de pile normale).</summary>
public class Entree
{
    /// <summary>La pile, ou null si son objet n'existe plus dans le jeu (mod retiré) : on garde alors les octets bruts.</summary>
    public ItemStack? Pile;
    public byte[]? Brut;
    public string Cle = "";
    /// <summary>Valeur de l'horloge du cylindre au moment de la dernière mise à jour de fraîcheur de la pile.</summary>
    public double Tampon;
}

/// <summary>
/// Contenu d'un cylindre-mémoire, conservé par le serveur (données de la partie), indépendamment de l'objet.
/// L'horloge compte les heures de pourrissement « effectives » : elle avance à la vitesse fixée par le réseau
/// (stabilisateur, cylindre réfrigéré…) et à vitesse normale quand le cylindre n'est relié à aucun cœur.
/// </summary>
public class Cylindre
{
    public string Id = "";
    public string Materiau = "cuivre";
    public int ObjetsMax = 2000;
    public int TypesMax = 25;
    public double Horloge;
    public double DernierReel = -1;
    public readonly List<Entree> Entrees = new();
    public bool Modifie;

    public long Objets => Entrees.Sum(e => (long)(e.Pile?.StackSize ?? 0));
    public int Types => Entrees.Count;

    public void AvancerHorloge(double maintenant, double taux)
    {
        if (DernierReel < 0) { DernierReel = maintenant; Modifie = true; return; }
        double dt = maintenant - DernierReel;
        if (dt <= 0) return;
        Horloge += dt * Math.Clamp(taux, 0, 1);
        DernierReel = maintenant;
        Modifie = true;
    }

    /// <summary>Met la fraîcheur d'une entrée à jour. Une viande devenue pourriture change d'entrée ; renvoie false si l'entrée a disparu.</summary>
    public bool Actualiser(Entree e, IWorldAccessor world)
    {
        if (e.Pile == null || !Transitions.APeremption(world, e.Pile)) return e.Pile != null;
        double maintenant = world.Calendar.TotalHours;
        double derniere = Transitions.DerniereMaj(e.Pile);
        float taux = 1f;
        if (!double.IsNaN(derniere))
        {
            double ecoule = maintenant - derniere;
            if (ecoule <= 0.05) return true; // le jeu ne met pas à jour en dessous de 3 minutes
            taux = (float)Math.Clamp((Horloge - e.Tampon) / ecoule, 0, 1);
        }
        // Le jeu transforme la pile sur place (SetFrom) : on retient l'objet d'origine pour voir s'il a changé.
        var objetAvant = e.Pile.Collectible;
        var resultat = Transitions.Avancer(world, e.Pile, taux);
        e.Tampon = Horloge;
        Modifie = true;
        if (resultat == null || resultat.StackSize <= 0) { Entrees.Remove(e); return false; }
        if (resultat.Collectible != objetAvant)
        {
            var cle = Transitions.Cle(resultat);
            if (cle != e.Cle)
            {
                Entrees.Remove(e);
                AjouterSansLimite(resultat, cle);
                return false;
            }
        }
        e.Pile = resultat;
        return true;
    }

    public void ActualiserTout(IWorldAccessor world)
    {
        foreach (var e in Entrees.ToArray())
            if (Entrees.Contains(e)) Actualiser(e, world);
    }

    public Entree? Trouver(string cle) => Entrees.FirstOrDefault(e => e.Cle == cle);

    public int PlaceLibre(string cle)
    {
        long libre = Math.Max(0, ObjetsMax - Objets);
        if (Trouver(cle) == null && Types >= TypesMax) return 0;
        return (int)Math.Min(int.MaxValue, libre);
    }

    /// <summary>Range une partie de la pile (déjà à jour de fraîcheur). Renvoie la quantité rangée.</summary>
    public int Ajouter(ItemStack pile, string cle, IWorldAccessor world)
    {
        int quantite = Math.Min(pile.StackSize, PlaceLibre(cle));
        if (quantite <= 0) return 0;
        var part = pile.Clone();
        part.StackSize = quantite;
        var existante = Trouver(cle);
        if (existante != null && !Actualiser(existante, world)) existante = Trouver(cle);
        if (existante?.Pile != null)
        {
            Transitions.Fusionner(existante.Pile, part);
            existante.Tampon = Horloge;
        }
        else
        {
            Entrees.Add(new Entree { Pile = part, Cle = cle, Tampon = Horloge });
        }
        Modifie = true;
        return quantite;
    }

    private void AjouterSansLimite(ItemStack pile, string cle)
    {
        var existante = Trouver(cle);
        if (existante?.Pile != null) { Transitions.Fusionner(existante.Pile, pile); existante.Tampon = Horloge; }
        else Entrees.Add(new Entree { Pile = pile, Cle = cle, Tampon = Horloge });
    }

    /// <summary>Retire jusqu'à quantite objets de la clé ; renvoie la pile retirée (à jour de fraîcheur) ou null.</summary>
    public ItemStack? Retirer(string cle, int quantite, IWorldAccessor world)
    {
        var e = Trouver(cle);
        if (e == null || !Actualiser(e, world) || e.Pile == null) return null;
        int n = Math.Min(quantite, e.Pile.StackSize);
        if (n <= 0) return null;
        var sortie = e.Pile.Clone();
        sortie.StackSize = n;
        e.Pile.StackSize -= n;
        if (e.Pile.StackSize <= 0) Entrees.Remove(e);
        Modifie = true;
        return sortie;
    }

    public byte[] VersOctets()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(1); // version du format
        w.Write(Id); w.Write(Materiau); w.Write(ObjetsMax); w.Write(TypesMax);
        w.Write(Horloge); w.Write(DernierReel);
        w.Write(Entrees.Count);
        foreach (var e in Entrees)
        {
            w.Write(e.Tampon);
            var octets = e.Pile != null ? Transitions.VersOctets(e.Pile) : e.Brut ?? Array.Empty<byte>();
            w.Write(octets.Length);
            w.Write(octets);
        }
        return ms.ToArray();
    }

    public static Cylindre DepuisOctets(byte[] donnees, IWorldAccessor world)
    {
        using var r = new BinaryReader(new MemoryStream(donnees));
        r.ReadInt32();
        var c = new Cylindre
        {
            Id = r.ReadString(), Materiau = r.ReadString(), ObjetsMax = r.ReadInt32(), TypesMax = r.ReadInt32(),
            Horloge = r.ReadDouble(), DernierReel = r.ReadDouble(),
        };
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            double tampon = r.ReadDouble();
            var octets = r.ReadBytes(r.ReadInt32());
            var pile = octets.Length > 0 ? Transitions.DepuisOctets(octets, world) : null;
            c.Entrees.Add(pile != null
                ? new Entree { Pile = pile, Cle = Transitions.Cle(pile), Tampon = tampon }
                : new Entree { Brut = octets, Cle = "inconnu-" + i, Tampon = tampon });
        }
        return c;
    }
}

/// <summary>Tous les cylindres connus du serveur, chargés à la demande et sauvegardés avec la partie.</summary>
public class RegistreCylindres
{
    private const string Prefixe = "curveostockage-cyl-";
    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<string, Cylindre> charges = new();

    public RegistreCylindres(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        sapi.Event.GameWorldSave += Sauvegarder;
    }

    public Cylindre Obtenir(string id, ItemStack objet)
    {
        if (!charges.TryGetValue(id, out var c))
        {
            var donnees = sapi.WorldManager.SaveGame.GetData(Prefixe + id);
            c = donnees != null ? Cylindre.DepuisOctets(donnees, sapi.World) : new Cylindre { Id = id, Modifie = true };
            charges[id] = c;
        }
        // Capacité et matériau viennent toujours de l'objet (réglables dans son JSON)
        c.Materiau = objet.Collectible.Variant["materiau"] ?? c.Materiau;
        var cap = objet.Collectible.Attributes?["capacite"];
        if (cap != null && cap.Exists)
        {
            c.ObjetsMax = cap["objets"].AsInt(c.ObjetsMax);
            c.TypesMax = cap["types"].AsInt(c.TypesMax);
        }
        return c;
    }

    public void Sauvegarder()
    {
        foreach (var c in charges.Values.Where(c => c.Modifie))
        {
            sapi.WorldManager.SaveGame.StoreData(Prefixe + c.Id, c.VersOctets());
            c.Modifie = false;
        }
    }
}
