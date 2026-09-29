using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>
/// Le cœur temporel : il connaît le réseau (baies, terminaux, stabilisateur), fait avancer les horloges des
/// cylindres à la bonne vitesse et réalise les dépôts, retraits et listes demandés par les terminaux.
/// </summary>
public class BECoeur : BlockEntity
{
    private double dernierTick = -1;
    private bool premierTick = true;
    private int revisionVue = -1;
    private long derniereExploration;
    private Reseau.Carte? carte;
    private HashSet<string> relies = new();
    private int modeCourant;
    private bool stabilisateurActif;
    private bool envoiPrevu;

    // Résumé synchronisé vers les joueurs pour l'info-bulle du bloc
    private string? erreur;
    private int nbCylindres, types, typesMax;
    private long objets, objetsMax;

    private static ConfigStockage Cfg => StockageSystem.Config;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(_ => MiseAJour(), 2000);
    }

    /// <summary>Les cylindres présents dans les baies du réseau, avec l'emplacement qui les contient.</summary>
    private List<(Cylindre c, ItemSlot slot)> Presents()
    {
        var liste = new List<(Cylindre, ItemSlot)>();
        if (carte == null) return liste;
        var registre = StockageSystem.De(Api).Registre!;
        foreach (var pos in carte.Baies)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BEBaie baie) continue;
            foreach (var slot in baie.Inventory)
            {
                var pile = slot.Itemstack;
                if (pile?.Collectible is not ItemCylindre) continue;
                var id = pile.Attributes.GetString("cylId");
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    pile.Attributes.SetString("cylId", id);
                    slot.MarkDirty();
                }
                liste.Add((registre.Obtenir(id, pile), slot));
            }
        }
        return liste;
    }

    /// <summary>Les bus de stockage du réseau qui visent un conteneur utilisable.</summary>
    private List<BEBusStockage> Externes()
    {
        var liste = new List<BEBusStockage>();
        if (carte == null) return liste;
        foreach (var pos in carte.BusStockage)
            if (Cfg.Bus.Stockage.Actif && Api.World.BlockAccessor.GetBlockEntity(pos) is BEBusStockage bus && bus.Cible() != null) liste.Add(bus);
        return liste;
    }
    private long signatureExternes;

    // File de fabrication du réseau (automates horlogers), sauvegardée avec le cœur
    public readonly List<Commande> Commandes = new();
    private int prochaineCommande = 1;

    public List<BlockPos> Automates() => carte?.Automates ?? new List<BlockPos>();

    /// <summary>Ajoute une commande à la fin de la file, ou devant (ingrédient d'une autre commande).</summary>
    public void AjouterCommande(Commande c, bool devant)
    {
        c.Id = prochaineCommande++;
        if (devant) Commandes.Insert(0, c); else Commandes.Add(c);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    public void RetirerCommande(Commande c)
    {
        Commandes.Remove(c);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    public void ViderCommandes()
    {
        Commandes.Clear();
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>Le réseau est utilisable et compte au moins un atelier.</summary>
    public bool AAtelier() => Cfg.Atelier.Actif && erreur == null && carte?.Ateliers.Count > 0;

    private BEStabilisateur? Stabilisateur()
        => !Cfg.Stabilisateur.Actif ? null : carte?.Stabilisateurs.Select(p => Api.World.BlockAccessor.GetBlockEntity(p) as BEStabilisateur).FirstOrDefault(s => s != null);

    private bool Tempete() => Api.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData?.nowStormActive == true;

    /// <summary>Vitesse du pourrissement dans un cylindre, selon son matériau et le stabilisateur (actif = part du temps alimenté).</summary>
    /// <remarks>Le cylindre de stase est aussi réfrigéré : hors stase alimentée (mode normal, plus d'engrenages), il ralentit comme un réfrigéré.</remarks>
    public static double Taux(string materiau, int mode, double actif)
    {
        materiau = Cfg.Effet(materiau);
        var cyl = Cfg.Cylindres;
        double base_ = materiau == "refrigere" ? cyl.FacteurRefrigere : materiau == "stase" ? cyl.FacteurStaseRepos : 1;
        if (mode == 3 && materiau == "stase") return actif * cyl.FacteurStaseFige + (1 - actif) * base_;
        double f = Cfg.Stabilisateur.Mode(mode)?.Facteur ?? 1;
        return base_ * (actif * f + (1 - actif));
    }

    /// <summary>Met le réseau et les horloges à l'instant présent. Appelé toutes les 2 s et avant chaque opération.</summary>
    public void MiseAJour()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        double maintenant = Api.World.Calendar.TotalHours;
        // Réexploré à chaque pose/retrait de bloc, et toutes les 30 s pour voir les parties rechargées avec leur chunk
        if (carte == null || revisionVue != StockageSystem.Revision || Api.World.ElapsedMilliseconds - derniereExploration > 30_000)
        {
            carte = Reseau.Explorer(Api.World, Pos, Cfg.Reseau.BlocsMax);
            revisionVue = StockageSystem.Revision;
            derniereExploration = Api.World.ElapsedMilliseconds;
        }
        erreur = carte.TropGrand ? "curveostockage:erreur-trop-grand"
            : carte.Coeurs.Count > 1 ? "curveostockage:erreur-plusieurs-coeurs" : null;
        var presents = erreur == null ? Presents() : new();

        if (dernierTick < 0) dernierTick = maintenant;
        double dt = Math.Max(0, maintenant - dernierTick);
        var stab = erreur == null ? Stabilisateur() : null;
        double actif = 0;
        modeCourant = stab?.Mode ?? 0;
        if (stab != null) actif = stab.Consommer(dt, Tempete());
        stabilisateurActif = stab != null && stab.Mode > 0 && (stab.Carburant > 0 || !Cfg.Stabilisateur.Consomme);

        foreach (var (c, _) in presents)
        {
            // Juste relié : il était hors réseau, le temps a passé normalement pour lui
            double taux = premierTick || relies.Contains(c.Id) ? Taux(c.Materiau, modeCourant, actif) : 1.0;
            c.AvancerHorloge(maintenant, taux);
        }
        relies = presents.Select(p => p.c.Id).ToHashSet();
        if (erreur == null)
        {
            Reequilibrer(presents);
            // Un coffre relié par un bus de stockage a changé (à la main, par une trémie…) : les terminaux ouverts se mettent à jour
            long sig = 17;
            foreach (var bus in Externes()) sig = sig * 31 + bus.Signature();
            if (sig != signatureExternes) { signatureExternes = sig; if (!premierTick) Changement(); }
        }
        premierTick = false;
        dernierTick = maintenant;
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();

        MajResume(presents);
    }

    private void MajResume(List<(Cylindre c, ItemSlot slot)> presents)
    {
        long o = 0, om = 0; int t = 0, tm = 0;
        foreach (var (c, slot) in presents)
        {
            long co = c.Objets;
            o += co; om += c.ObjetsMax; t += c.Types; tm += c.TypesMax;
            var attr = slot.Itemstack!.Attributes;
            var top = c.Entrees.Where(e => e.Pile != null).OrderByDescending(e => e.Pile!.StackSize).Take(5).ToList();
            var codes = top.Select(e => EvenementJournal.CodeDe(e.Pile!)).ToArray();
            var qtes = top.Select(e => e.Pile!.StackSize).ToArray();
            var anciensCodes = (attr["cylTopCodes"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
            var anciennesQtes = (attr["cylTopQte"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
            if (attr.GetLong("cylObjets", -1) != co || attr.GetInt("cylTypes", -1) != c.Types
                || !codes.SequenceEqual(anciensCodes) || !qtes.SequenceEqual(anciennesQtes))
            {
                attr.SetLong("cylObjets", co);
                attr.SetInt("cylTypes", c.Types);
                attr["cylTopCodes"] = new StringArrayAttribute(codes);
                attr["cylTopQte"] = new IntArrayAttribute(qtes);
                slot.MarkDirty();
            }
        }
        if (o != objets || om != objetsMax || t != types || tm != typesMax || presents.Count != nbCylindres || erreur != erreurEnvoyee)
        {
            objets = o; objetsMax = om; types = t; typesMax = tm; nbCylindres = presents.Count; erreurEnvoyee = erreur;
            MarkDirty();
        }
    }
    private string? erreurEnvoyee;

    // Journal d'activité (24 h, 40 évènements au plus) et alertes de pourriture déjà signalées
    private readonly List<EvenementJournal> journal = new();
    private readonly HashSet<string> alertes = new();
    public IReadOnlyList<EvenementJournal> Journal => journal;

    public void Journaliser(string? qui, int type, ItemStack? pile, int quantite)
    {
        if (type <= 1 && string.IsNullOrEmpty(qui)) return;
        long maintenant = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string code = pile == null ? "" : EvenementJournal.CodeDe(pile);
        var dernier = journal.Count > 0 ? journal[^1] : null;
        // Regroupe les maj+clics successifs du même joueur sur le même objet
        if (dernier != null && type <= 1 && dernier.Type == type && dernier.Qui == qui && dernier.Code == code && maintenant - dernier.Temps < 60_000)
        { dernier.Quantite += quantite; dernier.Temps = maintenant; }
        else journal.Add(new EvenementJournal { Temps = maintenant, Qui = qui ?? "", Type = type, Code = code, Quantite = quantite });
        journal.RemoveAll(e => maintenant - e.Temps > Cfg.Reseau.JournalHeures * 3600_000L);
        while (journal.Count > Math.Max(1, Cfg.Reseau.JournalEvenementsMax)) journal.RemoveAt(0);
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
    }

    /// <summary>null si le réseau est utilisable, sinon la clé de traduction de l'erreur.</summary>
    public string? Pret()
    {
        MiseAJour();
        if (erreur != null) return erreur;
        return nbCylindres == 0 && Externes().Count == 0 ? "curveostockage:erreur-sans-cylindre" : null;
    }

    /// <summary>
    /// Préférence de matériau (0 = idéal) : les denrées vont en stase, puis en réfrigéré, puis ailleurs, quel que soit
    /// le mode du moment ; le reste évite la stase et le réfrigéré pour leur laisser la place.
    /// </summary>
    private static int RangMateriau(string materiau, bool perissable) => perissable
        ? Cfg.Effet(materiau) switch { "stase" => 0, "refrigere" => 1, _ => 2 }
        : Cfg.Effet(materiau) switch { "stase" => 2, "refrigere" => 1, _ => 0 };

    private static IEnumerable<Cylindre> Ordonner(List<(Cylindre c, ItemSlot slot)> presents, string cle, bool perissable)
        => presents.Select(p => p.c).OrderBy(c => RangMateriau(c.Materiau, perissable)).ThenBy(c => c.Trouver(cle) != null ? 0 : 1).ToList();

    /// <summary>
    /// Tri automatique : déplace peu à peu les objets rangés dans un cylindre moins adapté (viande dans le cuivre alors
    /// qu'un cylindre de stase a de la place, fibres qui occupent la stase…). Quelques entrées par passage.
    /// </summary>
    private void Reequilibrer(List<(Cylindre c, ItemSlot slot)> presents)
    {
        int maxDeplacements = Cfg.Reseau.DeplacementsParTri;
        if (!Cfg.Reseau.TriAutomatique || maxDeplacements <= 0 || presents.Count < 2) return;
        var world = Api.World;
        var cylindres = presents.Select(p => p.c).ToList();
        int faits = 0;
        foreach (var source in cylindres)
        {
            foreach (var e in source.Entrees.ToArray())
            {
                if (faits >= maxDeplacements) return;
                if (e.Pile == null || !source.Entrees.Contains(e)) continue;
                bool perissable = Transitions.APeremption(world, e.Pile);
                int rangActuel = RangMateriau(source.Materiau, perissable);
                if (rangActuel == 0) continue;
                var cible = cylindres.Where(c => c != source && RangMateriau(c.Materiau, perissable) < rangActuel && c.PlaceLibre(e.Cle) > 0)
                    .OrderBy(c => RangMateriau(c.Materiau, perissable)).ThenBy(c => c.Trouver(e.Cle) != null ? 0 : 1).FirstOrDefault();
                if (cible == null) continue;
                var part = source.Retirer(e.Cle, cible.PlaceLibre(e.Cle), world);
                if (part == null) continue;
                int n = cible.Ajouter(part, e.Cle, world);
                part.StackSize -= n;
                if (part.StackSize > 0) source.Ajouter(part, e.Cle, world);
                if (n > 0) faits++;
            }
        }
    }

    /// <summary>Range la pile dans le réseau ; sa quantité diminue de ce qui a été rangé. Renvoie la quantité rangée.</summary>
    public int Inserer(ItemStack pile, out string? refus, string? qui = null)
    {
        refus = Pret();
        if (refus != null || pile.StackSize <= 0) return 0;
        var world = Api.World;
        if (pile.Collectible.GetTemperature(world, pile) > Cfg.Reseau.TemperatureMax) { refus = "curveostockage:refus-chaud"; return 0; }
        if (pile.Collectible is ItemCylindre) { refus = "curveostockage:refus-cylindre"; return 0; }
        // La pile arrive d'un inventaire normal : on la met à jour à vitesse normale avant de la ranger
        if (Transitions.Avancer(world, pile, 1f) == null) { pile.StackSize = 0; return 0; }
        var cle = Transitions.Cle(pile);
        bool perissable = Transitions.APeremption(world, pile);
        int total = 0;
        // Ordre : les coffres réservés à cet objet (bus de stockage en liste blanche), les cylindres, puis les autres coffres
        var externes = Externes();
        foreach (var bus in externes.Where(b => b.Prioritaire(pile)))
        {
            if (pile.StackSize <= 0) break;
            total += bus.Inserer(pile);
        }
        foreach (var c in Ordonner(Presents(), cle, perissable))
        {
            if (pile.StackSize <= 0) break;
            int n = c.Ajouter(pile, cle, world);
            pile.StackSize -= n;
            total += n;
        }
        foreach (var bus in externes.Where(b => !b.Prioritaire(pile)))
        {
            if (pile.StackSize <= 0) break;
            total += bus.Inserer(pile);
        }
        if (total == 0) refus = "curveostockage:refus-plein";
        else { Journaliser(qui, 0, pile, total); Changement(); }
        return total;
    }

    /// <summary>Retire jusqu'à quantite objets de ce type ; renvoie la pile retirée (fraîcheur à jour) ou null.</summary>
    public ItemStack? Extraire(string cle, int quantite, string? qui = null)
    {
        if (Pret() != null || quantite <= 0) return null;
        ItemStack? sortie = null;
        void Prendre(ItemStack? part)
        {
            if (part == null) return;
            if (sortie == null) sortie = part;
            else Transitions.Fusionner(sortie, part);
        }
        // Les coffres d'abord (le pourrissement y est normal), les cylindres ensuite
        foreach (var bus in Externes())
        {
            if (sortie?.StackSize >= quantite) break;
            Prendre(bus.Extraire(cle, quantite - (sortie?.StackSize ?? 0)));
        }
        foreach (var (c, _) in Presents())
        {
            if (sortie?.StackSize >= quantite) break;
            if (c.Trouver(cle) == null) continue;
            Prendre(c.Retirer(cle, quantite - (sortie?.StackSize ?? 0), Api.World));
        }
        if (sortie != null) { Journaliser(qui, 1, sortie, sortie.StackSize); Changement(); }
        return sortie;
    }

    /// <summary>Tout le contenu du réseau, par type : clé, un exemplaire (quantité 1) et la quantité totale.</summary>
    public List<(string cle, ItemStack exemple, long quantite)> Contenu()
    {
        var parCle = new Dictionary<string, (ItemStack exemple, long quantite)>();
        if (Pret() != null) return new();
        void Compter(string cle, ItemStack pile)
        {
            if (parCle.TryGetValue(cle, out var v)) parCle[cle] = (v.exemple, v.quantite + pile.StackSize);
            else { var ex = pile.Clone(); ex.StackSize = 1; parCle[cle] = (ex, pile.StackSize); }
        }
        foreach (var (c, _) in Presents())
            foreach (var e in c.Entrees)
                if (e.Pile != null) Compter(e.Cle, e.Pile);
        foreach (var bus in Externes())
            foreach (var (cle, pile) in bus.Piles()) Compter(cle, pile);
        return parCle.Select(kv => (kv.Key, kv.Value.exemple, kv.Value.quantite)).ToList();
    }

    /// <summary>Un exemplaire (à jour de fraîcheur) et la quantité totale d'un type dans le réseau.</summary>
    public (ItemStack? exemple, long quantite) Decrire(string cle)
    {
        if (Pret() != null) return (null, 0);
        ItemStack? exemple = null;
        long quantite = 0;
        foreach (var (c, _) in Presents())
        {
            var e = c.Trouver(cle);
            if (e == null || !c.Actualiser(e, Api.World) || e.Pile == null) continue;
            exemple ??= e.Pile;
            quantite += e.Pile.StackSize;
        }
        foreach (var bus in Externes())
            foreach (var (c, pile) in bus.Piles())
            {
                if (c != cle) continue;
                exemple ??= pile;
                quantite += pile.StackSize;
            }
        return (exemple, quantite);
    }

    /// <summary>Remet une pile dans le réseau sans tenir compte des limites (restes d'un retrait qui n'a pas pu être donné).</summary>
    public void Restituer(ItemStack pile)
    {
        if (pile.StackSize <= 0) return;
        Inserer(pile, out _);
        if (pile.StackSize > 0)
            Api.World.SpawnItemEntity(pile, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    public PaquetListe Lister()
    {
        var paquet = new PaquetListe();
        if (Pret() is string e) { paquet.Erreur = e; paquet.Atelier = AAtelier(); return paquet; }
        var agregat = new Dictionary<string, EntreeListe>();
        var exemples = new Dictionary<string, ItemStack>();
        foreach (var (c, _) in Presents())
        {
            c.ActualiserTout(Api.World);
            float taux = (float)Taux(c.Materiau, modeCourant, stabilisateurActif ? 1 : 0);
            foreach (var entree in c.Entrees)
            {
                if (entree.Pile == null) continue;
                if (Transitions.Fraicheur(Api.World, entree.Pile) is float f)
                {
                    if (f < Cfg.Reseau.AlerteFraicheur) { if (alertes.Add(entree.Cle)) Journaliser(null, 2, entree.Pile, (int)(f * 100)); }
                    else alertes.Remove(entree.Cle);
                }
                if (agregat.TryGetValue(entree.Cle, out var existante)) existante.Quantite += entree.Pile.StackSize;
                else
                {
                    agregat[entree.Cle] = new EntreeListe { Cle = entree.Cle, Quantite = entree.Pile.StackSize, Taux = taux };
                    exemples[entree.Cle] = entree.Pile;
                }
            }
            paquet.Objets += c.Objets; paquet.ObjetsMax += c.ObjetsMax;
            paquet.Types += c.Types; paquet.TypesMax += c.TypesMax;
        }
        foreach (var bus in Externes())
        {
            foreach (var (cle, pile) in bus.Piles())
            {
                if (agregat.TryGetValue(cle, out var existante)) existante.Quantite += pile.StackSize;
                else
                {
                    agregat[cle] = new EntreeListe { Cle = cle, Quantite = pile.StackSize, Taux = bus.Taux(pile) };
                    exemples[cle] = pile;
                }
            }
            var (o, om, t, tm) = bus.Capacite();
            paquet.Objets += o; paquet.ObjetsMax += om; paquet.Types += t; paquet.TypesMax += tm;
        }
        foreach (var (cle, entree) in agregat)
        {
            var exemple = exemples[cle].Clone();
            exemple.StackSize = 1;
            entree.Pile = exemple.ToBytes();
            paquet.Entrees.Add(entree);
        }
        paquet.Mode = modeCourant;
        paquet.StabilisateurActif = stabilisateurActif;
        paquet.Journal = journal.ToList();
        paquet.Atelier = AAtelier();
        paquet.Commandes = Commandes.Count(c => !c.Stock);
        var dejaVues = new HashSet<string>();
        foreach (var c in Cfg.Automate.Actif ? Cartes.DuReseau(this) : Enumerable.Empty<ItemStack>())
        {
            if (ItemCarte.Sortie(Api.World, c) is not ItemStack sortie) continue;
            var cle = Transitions.Cle(sortie);
            if (!dejaVues.Add(cle)) continue;
            var ex = sortie.Clone(); ex.StackSize = 1;
            paquet.Fabricables.Add(new EntreeFabricable { Cle = cle, Pile = ex.ToBytes(), ParFabrication = sortie.StackSize });
        }
        return paquet;
    }

    /// <summary>Prévient les terminaux ouverts (regroupé : au plus un envoi par quart de seconde).</summary>
    public void Changement()
    {
        if (envoiPrevu) return;
        envoiPrevu = true;
        RegisterDelayedCallback(_ =>
        {
            envoiPrevu = false;
            if (carte == null) return;
            PaquetListe? liste = null;
            foreach (var pos in carte.Terminaux)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BETerminal t || !t.AUnSpectateur) continue;
                liste ??= Lister();
                t.EnvoyerListe(liste);
            }
            StockageSystem.De(Api).TablettesServeur?.Diffuser(Pos, () => liste ??= Lister());
        }, 250);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("dernierTick", dernierTick);
        tree["journal"] = new ByteArrayAttribute(Vintagestory.API.Util.SerializerUtil.Serialize(journal));
        tree.SetInt("nbCylindres", nbCylindres);
        tree.SetLong("objets", objets); tree.SetLong("objetsMax", objetsMax);
        tree.SetInt("types", types); tree.SetInt("typesMax", typesMax);
        if (erreur != null) tree.SetString("erreur", erreur);
        tree["commandes"] = new ByteArrayAttribute(Vintagestory.API.Util.SerializerUtil.Serialize(Commandes));
        tree.SetInt("prochaineCommande", prochaineCommande);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        dernierTick = tree.GetDouble("dernierTick", -1);
        if (tree["journal"] is ByteArrayAttribute j && j.value?.Length > 0)
            try { journal.Clear(); journal.AddRange(Vintagestory.API.Util.SerializerUtil.Deserialize<List<EvenementJournal>>(j.value)); } catch { }
        nbCylindres = tree.GetInt("nbCylindres");
        objets = tree.GetLong("objets"); objetsMax = tree.GetLong("objetsMax");
        types = tree.GetInt("types"); typesMax = tree.GetInt("typesMax");
        erreur = tree.GetString("erreur");
        if (tree["commandes"] is ByteArrayAttribute c && c.value?.Length > 0)
            try { Commandes.Clear(); Commandes.AddRange(Vintagestory.API.Util.SerializerUtil.Deserialize<List<Commande>>(c.value)); } catch { }
        prochaineCommande = Math.Max(1, tree.GetInt("prochaineCommande", 1));
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (erreur != null) { dsc.AppendLine(Lang.Get(erreur)); return; }
        dsc.AppendLine(Lang.Get("curveostockage:coeur-info", nbCylindres, objets, objetsMax, types, typesMax));
    }
}
