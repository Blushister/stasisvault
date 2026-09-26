using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>
/// Garde des colonnes de chunks chargées pour les ancres actives. Chaque colonne est comptée (plusieurs ancres
/// peuvent la partager) et on ne relâche que celles qu'on a nous-mêmes forcées : le spawn, que le jeu garde déjà,
/// n'est jamais touché. Relâcher = retirer de la liste « à garder » ; le jeu décharge ensuite normalement,
/// seulement s'il n'y a personne à côté.
/// </summary>
public class GestionAncres
{
    private const string CleSauvegarde = "curveostockage-ancres";
    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<BlockPos, (HashSet<long> colonnes, string? uid)> ancres = new();
    private readonly Dictionary<long, int> compte = new();
    private readonly HashSet<long> forceesParNous = new();
    private readonly HashSet<long>? forceesParLeJeu;
    private readonly MethodInfo? retirerForcee;

    public GestionAncres(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        var serveur = sapi.World;
        forceesParLeJeu = serveur.GetType().GetField("forceLoadedChunkColumns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(serveur) as HashSet<long>;
        retirerForcee = serveur.GetType().GetMethod("RemoveChunkColumnFromForceLoadedList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (forceesParLeJeu == null || retirerForcee == null)
            sapi.Logger.Warning("[curveostockage] liste des chunks forcés inaccessible : les ancres garderont leurs chunks jusqu'au redémarrage.");
        sapi.Event.GameWorldSave += Sauvegarder;
        sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, Reprendre);
    }

    public long Colonne(BlockPos pos)
    {
        int cs = GlobalConstants.ChunkSize;
        return sapi.WorldManager.MapChunkIndex2D(pos.X / cs, pos.Z / cs);
    }

    /// <summary>Au démarrage : recharge la colonne de chaque ancre active pour que son bloc reprenne la main.</summary>
    private void Reprendre()
    {
        var donnees = sapi.WorldManager.SaveGame.GetData(CleSauvegarde);
        if (donnees == null) return;
        using var r = new BinaryReader(new MemoryStream(donnees));
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var pos = new BlockPos(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            var uid = r.ReadString();
            Definir(pos, new HashSet<long> { Colonne(pos) }, uid.Length > 0 ? uid : null);
        }
        sapi.Logger.Notification($"[curveostockage] {n} ancre(s) temporelle(s) reprise(s)");
    }

    private void Sauvegarder()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var actives = ancres.Where(a => a.Value.colonnes.Count > 0).ToList();
        w.Write(actives.Count);
        foreach (var (pos, (_, uid)) in actives) { w.Write(pos.X); w.Write(pos.Y); w.Write(pos.Z); w.Write(pos.dimension); w.Write(uid ?? ""); }
        sapi.WorldManager.SaveGame.StoreData(CleSauvegarde, ms.ToArray());
    }

    /// <summary>Colonnes que cette ancre doit garder chargées (vide = aucune).</summary>
    public void Definir(BlockPos pos, HashSet<long> colonnes, string? uid)
    {
        var anciennes = ancres.TryGetValue(pos, out var a) ? a.colonnes : new HashSet<long>();
        foreach (var c in colonnes.Except(anciennes)) Retenir(c);
        foreach (var c in anciennes.Except(colonnes)) Relacher(c);
        if (colonnes.Count == 0) ancres.Remove(pos);
        else ancres[pos.Copy()] = (new HashSet<long>(colonnes), uid);
    }

    public bool EstRetenue(long colonne) => compte.ContainsKey(colonne);
    public bool EstForceeParNous(long colonne) => forceesParNous.Contains(colonne);
    public bool EstForceeParLeJeu(long colonne) => forceesParLeJeu?.Contains(colonne) == true;

    /// <summary>L'ancre active de ce joueur, s'il en a une.</summary>
    public BlockPos? AncreActiveDe(string uid, BlockPos? sauf = null)
        => ancres.FirstOrDefault(a => a.Value.uid == uid && a.Value.colonnes.Count > 0 && !a.Key.Equals(sauf)).Key;

    public int AncresActivesDe(string uid, BlockPos? sauf = null)
        => ancres.Count(a => a.Value.uid == uid && a.Value.colonnes.Count > 0 && !a.Key.Equals(sauf));

    private void Retenir(long colonne)
    {
        compte[colonne] = compte.GetValueOrDefault(colonne) + 1;
        if (compte[colonne] > 1) return;
        if (forceesParLeJeu?.Contains(colonne) == true) return; // déjà gardée par le jeu (spawn…)
        var cp = sapi.WorldManager.MapChunkPosFromChunkIndex2D(colonne);
        sapi.WorldManager.LoadChunkColumnPriority(cp.X, cp.Y, new ChunkLoadOptions { KeepLoaded = true });
        forceesParNous.Add(colonne);
    }

    private void Relacher(long colonne)
    {
        if (!compte.TryGetValue(colonne, out var n)) return;
        if (n > 1) { compte[colonne] = n - 1; return; }
        compte.Remove(colonne);
        if (forceesParNous.Remove(colonne)) retirerForcee?.Invoke(sapi.World, new object[] { colonne });
    }
}

/// <summary>
/// Ancre temporelle : brûle des engrenages temporels pour garder chargées les colonnes de chunks de son réseau
/// (dans la limite fixée), pour que la tablette et le stabilisateur fonctionnent sans joueur sur place.
/// Le propriétaire est le joueur qui y dépose des engrenages ; une ancre active par joueur.
/// </summary>
public class BEAncre : BEACarburant
{
    public override string InventoryClassName => "curveostockage-ancre";
    public string? ProprietaireUid, ProprietaireNom;
    public int NbColonnes;
    public bool Limitee;
    public int[] CarteGardees = Array.Empty<int>(), CarteReseau = Array.Empty<int>();

    private double dernierTick = -1;
    private int revisionVue = -1;
    private double dernierCalcul = -1;
    private HashSet<long> colonnes = new();
    private List<long> visees = new(), reseau = new();
    private int nbColonnesEnvoye = -1;
    private IPlayer? ouvreur;

    private GestionAncres? Gestion => StockageSystem.De(Api).Ancres;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(_ => Tick(), 5000);
    }

    private bool Tempete() => Api.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData?.nowStormActive == true;

    public void Tick()
    {
        double maintenant = Api.World.Calendar.TotalHours;
        if (dernierTick < 0) dernierTick = maintenant;
        double dt = Math.Max(0, maintenant - dernierTick);
        dernierTick = maintenant;
        if (Carburant > 0 && dt > 0)
            Carburant = Math.Max(0, Carburant - dt / Cfg.HeuresParEngrenageAncre * (Tempete() ? Cfg.MultiplicateurTempete : 1));

        if (revisionVue != StockageSystem.Revision || dernierCalcul < 0 || maintenant - dernierCalcul > 1) Calculer(maintenant);
        var voulues = Carburant > 0 ? visees.ToHashSet() : new HashSet<long>();
        if (!voulues.SetEquals(colonnes))
        {
            colonnes = voulues;
            Gestion?.Definir(Pos, colonnes, ProprietaireUid);
        }
        NbColonnes = colonnes.Count;
        CarteGardees = colonnes.Select(Case).Where(c => c >= 0).ToArray();
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        if (Math.Abs(Carburant - carburantEnvoye) >= 0.01 || NbColonnes != nbColonnesEnvoye || (Carburant == 0 && carburantEnvoye != 0))
        {
            carburantEnvoye = Carburant; nbColonnesEnvoye = NbColonnes;
            MarkDirty();
        }
    }

    /// <summary>Case de la carte 5×5 (0..24, l'ancre au centre) d'une colonne, ou -1 si hors carte.</summary>
    private int Case(long colonne)
    {
        var cp = Api.World is Vintagestory.API.Server.IServerWorldAccessor ? ((Vintagestory.API.Server.ICoreServerAPI)Api).WorldManager.MapChunkPosFromChunkIndex2D(colonne) : null;
        if (cp == null) return -1;
        int cs = GlobalConstants.ChunkSize;
        int dx = cp.X - Pos.X / cs, dz = cp.Y - Pos.Z / cs;
        return Math.Abs(dx) > 2 || Math.Abs(dz) > 2 ? -1 : (dz + 2) * 5 + dx + 2;
    }

    /// <summary>Les colonnes des blocs du réseau (au plus ColonnesMaxParAncre, les plus proches de l'ancre).</summary>
    private void Calculer(double maintenant)
    {
        revisionVue = StockageSystem.Revision;
        dernierCalcul = maintenant;
        var gestion = Gestion;
        if (gestion == null) return;
        var carte = Reseau.Explorer(Api.World, Pos, Cfg.BlocsMaxParReseau);
        int cs = GlobalConstants.ChunkSize;
        var toutes = carte.Noeuds.Append(Pos)
            .Select(p => (col: gestion.Colonne(p), dist: Math.Abs(p.X / cs - Pos.X / cs) + Math.Abs(p.Z / cs - Pos.Z / cs)))
            .GroupBy(x => x.col).Select(g => g.First())
            .OrderBy(x => x.dist).ToList();
        Limitee = toutes.Count > Cfg.ColonnesMaxParAncre;
        reseau = toutes.Select(x => x.col).ToList();
        visees = reseau.Take(Cfg.ColonnesMaxParAncre).ToList();
        CarteReseau = reseau.Select(Case).Where(c => c >= 0).ToArray();
    }

    protected override bool AvantAjout()
    {
        if (ouvreur is not Vintagestory.API.Server.IServerPlayer sp) return false;
        var autre = Gestion?.AncreActiveDe(sp.PlayerUID, Pos);
        if (autre != null && Gestion!.AncresActivesDe(sp.PlayerUID, Pos) >= Math.Max(1, Cfg.AncresParJoueur))
        {
            sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, "curveostockage:ancre-deja",
                autre.X - Api.World.DefaultSpawnPosition.XYZInt.X, autre.Y, autre.Z - Api.World.DefaultSpawnPosition.XYZInt.Z), EnumChatType.Notification);
            return false;
        }
        ProprietaireUid = sp.PlayerUID;
        ProprietaireNom = sp.PlayerName;
        return true;
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            toggleInventoryDialogClient(byPlayer, () => new GuiAncre((Vintagestory.API.Client.ICoreClientAPI)Api, this));
        return true;
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        if (packetid == 1000 || packetid < 1000) ouvreur = player;
        base.OnReceivedClientPacket(player, packetid, data);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (Api?.Side == EnumAppSide.Server) Gestion?.Definir(Pos, new HashSet<long>(), null);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("dernierTick", dernierTick);
        tree.SetInt("colonnes", NbColonnes);
        tree.SetBool("limitee", Limitee);
        tree["carteGardees"] = new IntArrayAttribute(CarteGardees);
        tree["carteReseau"] = new IntArrayAttribute(CarteReseau);
        if (ProprietaireUid != null) { tree.SetString("uid", ProprietaireUid); tree.SetString("nom", ProprietaireNom ?? ""); }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        dernierTick = tree.GetDouble("dernierTick", -1);
        NbColonnes = tree.GetInt("colonnes");
        Limitee = tree.GetBool("limitee");
        CarteGardees = (tree["carteGardees"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        CarteReseau = (tree["carteReseau"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        ProprietaireUid = tree.GetString("uid");
        ProprietaireNom = tree.GetString("nom");
        (invDialog as GuiAncre)?.Maj();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        dsc.AppendLine(Lang.Get("curveostockage:stab-info-carburant", Carburant.ToString("0.##"), Cfg.EngrenagesMax));
        dsc.AppendLine(Carburant <= 0 ? Lang.Get("curveostockage:ancre-info-inactive")
            : Lang.Get("curveostockage:ancre-info-active", NbColonnes, (Carburant * Cfg.HeuresParEngrenageAncre / 24).ToString("0.#")));
        if (Limitee) dsc.AppendLine(Lang.Get("curveostockage:ancre-info-limitee", Cfg.ColonnesMaxParAncre));
        if (!string.IsNullOrEmpty(ProprietaireNom)) dsc.AppendLine(Lang.Get("curveostockage:ancre-info-proprio", ProprietaireNom));
    }
}
