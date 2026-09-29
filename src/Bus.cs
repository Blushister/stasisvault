using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>Bus : bloc de réseau dont la tête vise un bloc voisin (variante « face » = direction de la tête).</summary>
public class BlockBus : BlockReseau
{
    public BlockFacing Face => BlockFacing.FromCode(Variant["face"]) ?? BlockFacing.NORTH;

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;
        // Posé contre un conteneur : la tête le vise. Posé contre un bloc du réseau : le dos s'y branche.
        var clique = blockSel.Position.AddCopy(blockSel.Face.Opposite);
        var face = world.BlockAccessor.GetBlock(clique) is IReseau ? blockSel.Face : blockSel.Face.Opposite;
        var bloc = world.GetBlock(CodeWithVariant("face", face.Code)) ?? this;
        if (!bloc.DoPlaceBlock(world, byPlayer, blockSel, itemstack)) return false;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEBus bus) bus.Proprietaire = byPlayer?.PlayerUID;
        return true;
    }

    /// <summary>Un bloc du réseau posé ou retiré à côté : les bras du bus changent, on redessine chez les joueurs.</summary>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side == EnumAppSide.Server) world.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
    }
}

/// <summary>
/// Case de filtre : garde une copie de l'objet cliqué, sans le prendre au joueur. Clic main vide : vide la case.
/// Avec quantité (bus d'export) : clic = taille de la pile tenue, clic droit = ±1.
/// </summary>
public class SlotFiltre : ItemSlot
{
    private readonly bool avecQuantite;

    public SlotFiltre(InventoryBase inventory, bool avecQuantite) : base(inventory)
    {
        this.avecQuantite = avecQuantite;
        MaxSlotStackSize = 9999;
    }

    public override bool CanHold(ItemSlot sourceSlot) => false;
    public override bool CanTake() => false;
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
    public override ItemStack TakeOutWhole() => null!;
    public override ItemStack TakeOut(int quantity) => null!;

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        bool droit = op.MouseButton == EnumMouseButton.Right;
        if (sourceSlot.Empty)
        {
            if (itemstack == null) return;
            if (avecQuantite && droit && itemstack.StackSize > 1) itemstack.StackSize--;
            else itemstack = null;
        }
        else if (avecQuantite && droit && itemstack?.Collectible == sourceSlot.Itemstack.Collectible)
            itemstack!.StackSize++;
        else
        {
            itemstack = sourceSlot.Itemstack.Clone();
            itemstack.StackSize = avecQuantite && !droit ? sourceSlot.StackSize : 1;
        }
        MarkDirty();
    }
}

/// <summary>Accès d'un joueur (même hors ligne) à un bloc : zones protégées et verrous.</summary>
public static class Acces
{
    public static bool Autorise(ICoreAPI api, string? uid, BlockPos pos)
    {
        if (api is not ICoreServerAPI sapi) return true;
        var groupes = uid == null ? new HashSet<int>()
            : sapi.PlayerData.GetPlayerDataByUid(uid)?.PlayerGroupMemberships?.Keys.ToHashSet() ?? new HashSet<int>();
        var zones = sapi.World.Claims.Get(pos);
        if (zones != null)
            foreach (var z in zones)
            {
                if (z.AllowUseEveryone) continue;
                if (uid == null) return false;
                if (z.OwnedByPlayerUid == uid) continue;
                if (z.OwnedByPlayerGroupUid != 0 && groupes.Contains((int)z.OwnedByPlayerGroupUid)) continue;
                if (z.PermittedPlayerUids.TryGetValue(uid, out var f) && (f & EnumBlockAccessFlags.Use) != 0) continue;
                if (groupes.Any(g => z.PermittedPlayerGroupIds.TryGetValue(g, out var fg) && (fg & EnumBlockAccessFlags.Use) != 0)) continue;
                return false;
            }
        var renfort = sapi.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.GetReinforcment(pos);
        if (renfort != null && renfort.Locked && renfort.PlayerUID != uid && !(renfort.GroupUid != 0 && groupes.Contains(renfort.GroupUid))) return false;
        return true;
    }
}

/// <summary>Échanges avec les conteneurs du jeu et des mods.</summary>
public static class Conteneurs
{
    /// <summary>Rangement (coffre, caisse, étagère, vitrine…) : tous ses emplacements sont du stockage. Sinon c'est une machine.</summary>
    public static bool Rangement(BlockEntityContainer be)
        => be is BlockEntityGenericTypedContainer or BlockEntityCrate || (be is BlockEntityDisplay && be is not BlockEntityGroundStorage);

    /// <summary>Emplacements où l'on peut prendre : tous pour un rangement, sinon celui que la machine donne à une trémie (sa sortie).</summary>
    public static List<ItemSlot> Sorties(BlockEntityContainer be, BlockFacing faceCible)
    {
        if (Rangement(be)) return be.Inventory.Where(s => !s.Empty && s.CanTake()).ToList();
        var s = be.Inventory.GetAutoPullFromSlot(faceCible);
        return s == null || s.Empty ? new() : new() { s };
    }

    /// <summary>Le conteneur accepterait-il au moins un exemplaire de cette pile ?</summary>
    public static bool PeutRanger(IWorldAccessor world, BlockEntityContainer be, BlockFacing faceCible, ItemStack pile)
    {
        var essai = pile.Clone(); essai.StackSize = 1;
        var source = new DummySlot(essai);
        if (!Rangement(be) || be is BlockEntityCrate) return be.Inventory.GetAutoPushIntoSlot(faceCible, source) != null;
        return be.Inventory.Any(s => s.Empty ? s.CanHold(source)
            : s.Itemstack.Equals(world, pile, GlobalConstants.IgnoredStackAttributes) && s.StackSize < s.Itemstack.Collectible.MaxStackSize);
    }

    /// <summary>Range la pile dans le conteneur ; sa quantité diminue de ce qui a été rangé. Renvoie la quantité rangée.</summary>
    public static int Ranger(IWorldAccessor world, BlockEntityContainer be, BlockFacing faceCible, ItemStack pile)
    {
        int avant = pile.StackSize;
        // Une copie : en rejoignant un emplacement vide, la pile elle-même peut devenir celle du conteneur
        var source = new DummySlot(pile.Clone());
        void Verser(ItemSlot cible)
        {
            if (source.Empty) return;
            if (source.TryPutInto(world, cible, source.StackSize) > 0) cible.MarkDirty();
        }
        if (!Rangement(be) || be is BlockEntityCrate)
        {
            // Machine ou caisse : le conteneur choisit l'emplacement, comme pour une trémie (une caisse = un seul type d'objet)
            for (int i = 0; i < 64 && !source.Empty; i++)
            {
                var cible = be.Inventory.GetAutoPushIntoSlot(faceCible, source);
                int restant = source.StackSize;
                if (cible == null) break;
                Verser(cible);
                if (source.StackSize == restant && !source.Empty) break;
            }
        }
        else
        {
            foreach (var slot in be.Inventory)
                if (!slot.Empty && slot.Itemstack.Equals(world, pile, GlobalConstants.IgnoredStackAttributes)) Verser(slot);
            foreach (var slot in be.Inventory)
                if (slot.Empty && slot.CanHold(source)) Verser(slot);
        }
        int reste = source.Itemstack?.StackSize ?? 0;
        pile.StackSize = reste;
        return avant - reste;
    }

    /// <summary>Retire jusqu'à quantite objets de cette clé d'un rangement ; fraîcheur mise à jour à la vitesse du conteneur.</summary>
    public static ItemStack? Prendre(IWorldAccessor world, BlockEntityContainer be, string cle, int quantite)
    {
        ItemStack? sortie = null;
        foreach (var slot in be.Inventory)
        {
            if (sortie?.StackSize >= quantite) break;
            if (slot.Empty || !slot.CanTake()) continue;
            if (Transitions.APeremption(world, slot.Itemstack)) slot.Itemstack.Collectible.UpdateAndGetTransitionStates(world, slot);
            if (slot.Empty || Transitions.Cle(slot.Itemstack) != cle) continue;
            var part = slot.TakeOut(Math.Min(quantite - (sortie?.StackSize ?? 0), slot.StackSize));
            slot.MarkDirty();
            if (part == null) continue;
            if (sortie == null) sortie = part;
            else Transitions.Fusionner(sortie, part);
        }
        return sortie;
    }
}

/// <summary>
/// Base des bus : quelques emplacements réels (tampon) suivis de 9 cases de filtre. Le bus retrouve son cœur
/// (mis en cache jusqu'au prochain changement de réseau) et vise le bloc devant sa tête.
/// </summary>
public abstract class BEBus : BlockEntityOpenableContainer
{
    public const int NbFiltres = 9;
    public const int PaquetListeNoire = 4100, PaquetRetraitSeul = 4101;

    protected readonly InventoryGeneric inventaire;
    public override InventoryBase Inventory => inventaire;

    /// <summary>Joueur qui a posé le bus : il n'agit que sur les conteneurs auxquels ce joueur a accès.</summary>
    public string? Proprietaire;
    public bool ListeNoire;
    /// <summary>Clé de traduction de l'état (vide = tout va bien), synchronisée pour la fenêtre et l'info-bulle.</summary>
    public string Etat = "";

    private BlockPos? coeurPos;
    private int revisionCoeur = -1;
    private long derniereRecherche;
    protected static ConfigStockage Cfg => StockageSystem.Config;

    public abstract int NbTampon { get; }
    public virtual bool FiltreAvecQuantite => false;
    public abstract string Titre { get; }

    protected BEBus(int nbTampon, bool avecQuantite)
    {
        inventaire = new InventoryGeneric(nbTampon + NbFiltres, null, null,
            (id, inv) => id < nbTampon ? new ItemSlot(inv) : new SlotFiltre(inv, avecQuantite));
    }

    public BlockFacing Face => (Block as BlockBus)?.Face ?? BlockFacing.NORTH;
    public BlockPos PosCible => Pos.AddCopy(Face);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventaire.OnGetAutoPullFromSlot = _ => null!;
        inventaire.OnGetAutoPushIntoSlot = (face, source) => AutoPousse(face, source)!;
        // Rien ne pourrit dans un bus : le tampon se vide aussitôt et les filtres ne sont que des modèles
        inventaire.OnAcquireTransitionSpeed += (_, _, _) => 0f;
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(_ => Tick(), IntervalleTick);
    }

    protected virtual int IntervalleTick => Math.Max(100, Cfg.Bus.IntervalleMs);
    /// <summary>Ce type de bus est activé dans la config.</summary>
    public abstract bool Actif { get; }
    protected virtual ItemSlot? AutoPousse(BlockFacing face, ItemSlot source) => null;
    public abstract void Tick();

    public IEnumerable<ItemStack> Filtres()
    {
        for (int i = NbTampon; i < inventaire.Count; i++)
            if (inventaire[i].Itemstack is ItemStack f) yield return f;
    }

    public bool Correspond(ItemStack pile) => Filtres().Any(f => f.Collectible.Code.Equals(pile.Collectible.Code));

    /// <summary>Filtre vide : tout passe. Sinon liste blanche (seulement ces objets) ou noire (tout sauf eux).</summary>
    public bool Accepte(ItemStack pile) => !Filtres().Any() || Correspond(pile) != ListeNoire;

    protected BECoeur? Coeur()
    {
        long maintenant = Api.World.ElapsedMilliseconds;
        if (revisionCoeur != StockageSystem.Revision || maintenant - derniereRecherche > 30_000)
        {
            coeurPos = Reseau.TrouverCoeur(Api.World, Pos, out _)?.Pos.Copy();
            revisionCoeur = StockageSystem.Revision;
            derniereRecherche = maintenant;
        }
        return coeurPos == null ? null : Api.World.BlockAccessor.GetBlockEntity(coeurPos) as BECoeur;
    }

    /// <summary>Le conteneur devant la tête, s'il existe, n'est pas un bloc du réseau et reste accessible au propriétaire.</summary>
    public BlockEntityContainer? Conteneur(out string? etat)
    {
        etat = null;
        if (Api.World.BlockAccessor.GetBlock(PosCible) is IReseau
            || Api.World.BlockAccessor.GetBlockEntity(PosCible) is not BlockEntityContainer be)
        {
            etat = "curveostockage:bus-sans-cible";
            return null;
        }
        if (Cfg.Bus.RespecterProtections && !Acces.Autorise(Api, Proprietaire, PosCible)) { etat = "curveostockage:bus-interdit"; return null; }
        return be;
    }

    protected void DefinirEtat(string? etat)
    {
        etat ??= "";
        if (etat == Etat) return;
        Etat = etat;
        MarkDirty();
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            toggleInventoryDialogClient(byPlayer, () => new GuiBus((ICoreClientAPI)Api, this));
        return true;
    }

    /// <summary>Ajoute au modèle un bras de conduit vers chaque bloc du réseau voisin (sauf devant la tête).</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            if (face == Face || Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face)) is not IReseau) continue;
            if (Bras(face, tessThreadTesselator) is MeshData bras) mesher.AddMeshData(bras);
        }
        return base.OnTesselation(mesher, tessThreadTesselator);
    }

    private MeshData? Bras(BlockFacing face, ITesselatorAPI tess)
    {
        var cache = StockageSystem.De(Api).BrasBus;
        string cle = Block.Code + "/" + face.Code;
        lock (cache) if (cache.TryGetValue(cle, out var m)) return m;
        var forme = Shape.TryGet(Api, new AssetLocation("curveostockage", $"shapes/block/conduit-bras-{face.Code[0]}.json"));
        if (forme == null) return null;
        tess.TesselateShape(Block, forme, out var mesh);
        lock (cache) cache[cle] = mesh;
        return mesh;
    }

#pragma warning disable CS0618 // change en 1.23 (voir Stabilisateur.Autorise)
    private bool Autorise(IPlayer joueur)
        => new CachedAccessPerms(Api.World, Pos, joueur).IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, validatePickRange: true, "curveostockage");
#pragma warning restore CS0618

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        if (packetid is not (PaquetListeNoire or PaquetRetraitSeul) || data?.Length != 1 || !Autorise(player)) return;
        Reglage(packetid, data[0] == 1);
        MarkDirty();
    }

    protected virtual void Reglage(int paquet, bool valeur)
    {
        if (paquet == PaquetListeNoire) ListeNoire = valeur;
    }

    /// <summary>Les filtres ne sont que des modèles : on ne les rend pas quand le bus est cassé.</summary>
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        for (int i = NbTampon; i < inventaire.Count; i++) inventaire[i].Itemstack = null;
        base.OnBlockBroken(byPlayer);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Proprietaire != null) tree.SetString("proprietaire", Proprietaire);
        tree.SetBool("listeNoire", ListeNoire);
        tree.SetString("etat", Etat);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Proprietaire = tree.GetString("proprietaire");
        ListeNoire = tree.GetBool("listeNoire");
        Etat = tree.GetString("etat", "");
        (invDialog as GuiBus)?.Maj();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        var bloc = Api.World.BlockAccessor.GetBlock(PosCible);
        dsc.AppendLine(Lang.Get("curveostockage:bus-info-cible", bloc.Id == 0 ? Lang.Get("curveostockage:bus-rien") : bloc.GetPlacedBlockName(Api.World, PosCible)));
        int n = Filtres().Count();
        if (n > 0) dsc.AppendLine(Lang.Get("curveostockage:bus-info-filtre", n, Lang.Get(FiltreAvecQuantite ? "curveostockage:filtre-quantites"
            : ListeNoire ? "curveostockage:liste-noire" : "curveostockage:liste-blanche").ToLowerInvariant()));
        if (Etat.Length > 0) dsc.AppendLine(Lang.Get(Etat));
    }
}

/// <summary>
/// Bus d'import : aspire vers le réseau le contenu du conteneur visé (la sortie, pour une machine), et range
/// aussitôt tout ce qu'on pose dans sa case d'entrée, y compris ce qu'y poussent trémies et goulottes.
/// </summary>
public class BEBusImport : BEBus
{
    private bool absorbe;
    public BEBusImport() : base(1, false) { }
    public override int NbTampon => 1;
    public override string InventoryClassName => "curveostockage-busimport";
    public override string Titre => Lang.Get("curveostockage:busimport-titre");
    public override bool Actif => Cfg.Bus.Import.Actif;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) inventaire.SlotModified += i => { if (i == 0) Absorber(); };
    }

    protected override ItemSlot? AutoPousse(BlockFacing face, ItemSlot source)
    {
        var tampon = inventaire[0];
        if (!Actif || source.Itemstack == null || !Accepte(source.Itemstack)) return null;
        return tampon.Empty || tampon.Itemstack.Equals(Api.World, source.Itemstack, GlobalConstants.IgnoredStackAttributes) ? tampon : null;
    }

    private void Absorber()
    {
        var slot = inventaire[0];
        if (absorbe || slot.Empty) return;
        var coeur = Coeur();
        if (coeur == null) return;
        absorbe = true;
        try
        {
            coeur.Inserer(slot.Itemstack, out _);
            if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            slot.MarkDirty();
        }
        finally { absorbe = false; }
    }

    public override void Tick()
    {
        if (!Actif) { DefinirEtat("curveostockage:desactive"); return; }
        var coeur = Coeur();
        string? etat = coeur == null ? "curveostockage:erreur-sans-coeur" : null;
        if (coeur != null) Absorber();
        var be = Conteneur(out var etatCible);
        // Sans conteneur en face, le bus reste utile avec une trémie : ce n'est pas une erreur
        if (etatCible == "curveostockage:bus-interdit") etat ??= etatCible;
        if (coeur != null && be != null)
        {
            int reste = Math.Max(1, Cfg.Bus.ObjetsParTransfert);
            bool bouge = false;
            foreach (var slot in Conteneurs.Sorties(be, Face.Opposite))
            {
                if (reste <= 0) break;
                if (slot.Empty || !Accepte(slot.Itemstack)) continue;
                var pile = slot.Itemstack.Clone();
                pile.StackSize = Math.Min(reste, slot.StackSize);
                int n = coeur.Inserer(pile, out var refus);
                if (n <= 0)
                {
                    // Réseau plein ou en erreur : inutile d'essayer le reste
                    if (refus is "curveostockage:refus-plein" or "curveostockage:erreur-sans-cylindre" or "curveostockage:erreur-trop-grand" or "curveostockage:erreur-plusieurs-coeurs")
                    { etat ??= refus; break; }
                    continue;
                }
                slot.TakeOut(n);
                slot.MarkDirty();
                reste -= n;
                bouge = true;
            }
            if (bouge) be.MarkDirty(true);
        }
        DefinirEtat(etat);
    }
}

/// <summary>
/// Bus d'export : garde dans le conteneur visé la quantité indiquée de chaque objet du filtre, en la prenant
/// dans le réseau. Une machine choisit elle-même l'emplacement (combustible, entrée…), comme avec une trémie.
/// </summary>
public class BEBusExport : BEBus
{
    public BEBusExport() : base(0, true) { }
    public override int NbTampon => 0;
    public override bool FiltreAvecQuantite => true;
    public override string InventoryClassName => "curveostockage-busexport";
    public override string Titre => Lang.Get("curveostockage:busexport-titre");
    public override bool Actif => Cfg.Bus.Export.Actif;

    public override void Tick()
    {
        if (!Actif) { DefinirEtat("curveostockage:desactive"); return; }
        var coeur = Coeur();
        var be = Conteneur(out var etatCible);
        string? etat = coeur == null ? "curveostockage:erreur-sans-coeur" : etatCible;
        if (coeur != null && be != null && Filtres().Any())
        {
            var world = Api.World;
            var contenu = coeur.Contenu();
            int reste = Math.Max(1, Cfg.Bus.ObjetsParTransfert);
            bool bouge = false;
            foreach (var f in Filtres().ToList())
            {
                if (reste <= 0) break;
                long present = be.Inventory.Where(s => !s.Empty && s.Itemstack.Collectible.Code.Equals(f.Collectible.Code)).Sum(s => (long)s.StackSize);
                int manque = (int)Math.Min(reste, f.StackSize - present);
                foreach (var (cle, exemple, quantite) in contenu.Where(c => c.exemple.Collectible.Code.Equals(f.Collectible.Code)))
                {
                    if (manque <= 0) break;
                    if (!Conteneurs.PeutRanger(world, be, Face.Opposite, exemple)) break;
                    var pile = coeur.Extraire(cle, (int)Math.Min(manque, quantite));
                    if (pile == null) continue;
                    int n = Conteneurs.Ranger(world, be, Face.Opposite, pile);
                    if (pile.StackSize > 0) coeur.Restituer(pile);
                    manque -= n; reste -= n;
                    if (n > 0) bouge = true; else break;
                }
            }
            if (bouge) be.MarkDirty(true);
        }
        DefinirEtat(etat);
    }
}

/// <summary>
/// Bus de stockage : le contenu du rangement visé (coffre, caisse, étagère…) fait partie du réseau. Le filtre
/// choisit ce que le réseau peut y déposer ; en liste blanche, ce rangement est rempli avant les cylindres.
/// </summary>
public class BEBusStockage : BEBus
{
    public bool RetraitSeul;
    private BlockEntityContainer? cible;
    private long cibleLue = -1;

    public BEBusStockage() : base(0, false) { }
    public override int NbTampon => 0;
    public override string InventoryClassName => "curveostockage-busstockage";
    public override string Titre => Lang.Get("curveostockage:busstockage-titre");
    public override bool Actif => Cfg.Bus.Stockage.Actif;
    protected override int IntervalleTick => 2000;

    /// <summary>Le rangement visé (relu au plus une fois par tick serveur : le cœur le demande souvent).</summary>
    public BlockEntityContainer? Cible()
    {
        long t = Api.World.ElapsedMilliseconds;
        if (t != cibleLue)
        {
            var be = Actif ? Conteneur(out _) : null;
            cible = be != null && Conteneurs.Rangement(be) ? be : null;
            cibleLue = t;
        }
        return cible;
    }

    public bool Prioritaire(ItemStack pile) => !RetraitSeul && !ListeNoire && Filtres().Any() && Correspond(pile);

    public int Inserer(ItemStack pile)
    {
        if (RetraitSeul || !Accepte(pile) || Cible() is not BlockEntityContainer be) return 0;
        int n = Conteneurs.Ranger(Api.World, be, Face.Opposite, pile);
        if (n > 0) be.MarkDirty(true);
        return n;
    }

    public ItemStack? Extraire(string cle, int quantite)
    {
        if (Cible() is not BlockEntityContainer be) return null;
        var pile = Conteneurs.Prendre(Api.World, be, cle, quantite);
        if (pile != null) be.MarkDirty(true);
        return pile;
    }

    public IEnumerable<(string cle, ItemStack pile)> Piles()
    {
        if (Cible() is not BlockEntityContainer be) yield break;
        foreach (var slot in be.Inventory)
            if (!slot.Empty) yield return (Transitions.Cle(slot.Itemstack), slot.Itemstack);
    }

    public float Taux(ItemStack pile) => Cible()?.Inventory.GetTransitionSpeedMul(EnumTransitionType.Perish, pile) ?? 1f;

    /// <summary>Objets, place (une pile par emplacement vide), emplacements occupés et emplacements.</summary>
    public (long objets, long objetsMax, int types, int typesMax) Capacite()
    {
        long o = 0, om = 0; int t = 0, tm = 0;
        if (Cible() is BlockEntityContainer be)
            foreach (var slot in be.Inventory)
            {
                tm++;
                if (slot.Empty) { om += 64; continue; }
                t++;
                o += slot.StackSize;
                om += Math.Max(slot.StackSize, slot.Itemstack.Collectible.MaxStackSize);
            }
        return (o, om, t, tm);
    }

    /// <summary>Empreinte du contenu : change dès qu'un emplacement change d'objet ou de quantité.</summary>
    public long Signature()
    {
        long sig = Pos.GetHashCode();
        if (Cible() is BlockEntityContainer be)
            for (int i = 0; i < be.Inventory.Count; i++)
            {
                var s = be.Inventory[i].Itemstack;
                sig = sig * 31 + (s == null ? 0 : (s.Collectible.Id * 7919L + s.StackSize) * (i + 1));
            }
        return sig;
    }

    public override void Tick()
    {
        if (!Actif) { DefinirEtat("curveostockage:desactive"); return; }
        var coeur = Coeur();
        var be = Conteneur(out var etatCible);
        string? etat = coeur == null ? "curveostockage:erreur-sans-coeur"
            : etatCible ?? (be != null && !Conteneurs.Rangement(be) ? "curveostockage:bus-incompatible" : null);
        DefinirEtat(etat);
    }

    protected override void Reglage(int paquet, bool valeur)
    {
        base.Reglage(paquet, valeur);
        if (paquet == PaquetRetraitSeul) RetraitSeul = valeur;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("retraitSeul", RetraitSeul);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        RetraitSeul = tree.GetBool("retraitSeul");
        base.FromTreeAttributes(tree, worldForResolving);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (RetraitSeul) dsc.AppendLine(Lang.Get("curveostockage:acces-retrait"));
    }
}
