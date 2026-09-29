using System.Text;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace CurveoStockage;

/// <summary>
/// Carte perforée : une recette de grille (nom du fichier de recette, objet fabriqué, index indicatif) et la quantité
/// à garder en stock dans le réseau (0 = seulement sur commande). La carte vierge n'a aucun de ces attributs.
/// </summary>
public class ItemCarte : Item
{
    public static bool Perforee(ItemStack? p) => p?.Collectible is ItemCarte && p.Attributes.HasAttribute("recNom");
    public static string Ident(ItemStack p) => p.Attributes.GetString("recNom", "") + "|" + p.Attributes.GetString("recSortie", "");
    public static int Garder(ItemStack p) => p.Attributes.GetInt("garder");

    public static void Graver(ItemStack carte, GridRecipe r, int index)
    {
        carte.Attributes.SetString("recNom", r.Name?.ToString() ?? "");
        carte.Attributes.SetString("recSortie", PaquetRemplir.CodeSortie(r));
        carte.Attributes.SetInt("recIndex", index);
    }

    /// <summary>L'objet fabriqué (quantité = une fabrication), ou null si la recette n'existe plus.</summary>
    public static ItemStack? Sortie(IWorldAccessor world, ItemStack carte)
        => Cartes.Recettes(world, carte).FirstOrDefault()?.Output?.ResolvedItemStack;

    public override string GetHeldItemName(ItemStack itemStack)
    {
        if (!Perforee(itemStack)) return base.GetHeldItemName(itemStack);
        var sortie = Sortie(api.World, itemStack);
        return Lang.Get("curveostockage:carte-perforee-nom", sortie?.GetName() ?? itemStack.Attributes.GetString("recSortie"));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var carte = inSlot.Itemstack;
        if (!Perforee(carte)) { dsc.AppendLine(Lang.Get("curveostockage:carte-vierge-info")); return; }
        var r = Cartes.Recettes(world, carte!).FirstOrDefault();
        if (r?.Output?.ResolvedItemStack is not ItemStack sortie) { dsc.AppendLine(Lang.Get("curveostockage:carte-inconnue")); return; }
        dsc.AppendLine(Lang.Get("curveostockage:carte-fabrique", sortie.StackSize, sortie.GetName()));
        foreach (var g in (r.ResolvedIngredients ?? Array.Empty<CraftingRecipeIngredient?>()).OfType<CraftingRecipeIngredient>().GroupBy(i => i))
        {
            var ex = g.Key.ResolvedItemStack;
            string nom = ex?.GetName() ?? g.Key.Code?.ToString() ?? "?";
            if (g.Key.MatchingType != EnumRecipeMatchType.Exact) nom = Lang.Get("curveostockage:carte-au-choix", nom);
            dsc.AppendLine("  • " + (g.Key.ConsumeProperties.Consume ? $"{g.Key.Quantity * g.Count()} × " : "") + nom);
        }
        int garder = Garder(carte!);
        dsc.AppendLine(garder > 0 ? Lang.Get("curveostockage:carte-garder", garder) : Lang.Get("curveostockage:carte-commande"));
    }
}

/// <summary>Emplacement d'automate : n'accepte qu'une carte perforée.</summary>
public class SlotCarte : ItemSlot
{
    public SlotCarte(InventoryBase inventory) : base(inventory) { MaxSlotStackSize = 1; }
    public override bool CanHold(ItemSlot sourceSlot) => ItemCarte.Perforee(sourceSlot?.Itemstack) && base.CanHold(sourceSlot!);
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => ItemCarte.Perforee(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot!, priority);
}

/// <summary>Une commande de fabrication d'un réseau : une carte (recette) et le nombre de fabrications restantes.</summary>
[ProtoContract]
public class Commande
{
    [ProtoMember(1)] public int Id;
    [ProtoMember(2)] public string Carte = "";
    [ProtoMember(3)] public int Restant;
    [ProtoMember(4)] public int Total;
    /// <summary>Commande qui attend celle-ci (fabrication d'un ingrédient), 0 sinon.</summary>
    [ProtoMember(5)] public int Parent;
    /// <summary>Joueur qui a commandé, vide pour le maintien du stock.</summary>
    [ProtoMember(6)] public string Qui = "";
    [ProtoMember(7)] public bool Stock;
    /// <summary>Ce qui bloque (nom de l'ingrédient manquant), vide si tout va bien.</summary>
    [ProtoMember(8)] public string Manque = "";
    [ProtoMember(9)] public string NomSortie = "";
}

/// <summary>Recettes des cartes, fabrication d'une recette avec le contenu du réseau, calcul de ce qui manque.</summary>
public static class Cartes
{
    public static int ProfondeurMax => Math.Max(0, StockageSystem.Config.Automate.ProfondeurMax);
    private static readonly Dictionary<string, List<GridRecipe>> cache = new();
    private static IWorldAccessor? mondeCache;

    /// <summary>Les recettes de la carte : celle de l'index indiqué si elle correspond, puis ses variantes (même fichier, même objet).</summary>
    public static List<GridRecipe> Recettes(IWorldAccessor world, ItemStack carte)
    {
        string ident = ItemCarte.Ident(carte);
        lock (cache)
        {
            if (mondeCache != world) { cache.Clear(); mondeCache = world; }
            if (cache.TryGetValue(ident, out var connues)) return connues;
        }
        string nom = carte.Attributes.GetString("recNom", ""), sortie = carte.Attributes.GetString("recSortie", "");
        int index = carte.Attributes.GetInt("recIndex", -1);
        bool Correspond(GridRecipe x) => x.ResolvedIngredients != null && (x.Name?.ToString() ?? "") == nom && PaquetRemplir.CodeSortie(x) == sortie;
        var liste = world.GridRecipes;
        var r = new List<GridRecipe>();
        if (index >= 0 && index < liste.Count && Correspond(liste[index])) r.Add(liste[index]);
        foreach (var x in liste) if (Correspond(x) && !r.Contains(x)) r.Add(x);
        lock (cache) cache[ident] = r;
        return r;
    }

    /// <summary>Ingrédients de la recette dans la grille 3×3 (comme l'atelier).</summary>
    public static CraftingRecipeIngredient?[] Cases(GridRecipe r)
    {
        var cases = new CraftingRecipeIngredient?[9];
        var ings = r.ResolvedIngredients!;
        if (r.Shapeless) { int k = 0; foreach (var i in ings) if (i != null && k < 9) cases[k++] = i; }
        else for (int y = 0; y < r.Height && y < 3; y++) for (int x = 0; x < r.Width && x < 3; x++) cases[y * 3 + x] = ings[y * r.Width + x];
        return cases;
    }

    private static int Besoin(CraftingRecipeIngredient ing) => ing.ConsumeProperties.Consume ? ing.Quantity : 1;

    /// <summary>
    /// Fabrique une fois la recette de la carte avec le contenu du réseau ; le résultat et ce que rendent les
    /// ingrédients (seaux…) retournent dans le réseau. Renvoie la quantité fabriquée, ou 0 et l'ingrédient qui manque.
    /// </summary>
    public static (int fabrique, CraftingRecipeIngredient? manque) Fabriquer(BECoeur coeur, ItemStack carte)
    {
        var world = coeur.Api.World;
        var recettes = Recettes(world, carte);
        CraftingRecipeIngredient? premierManque = null;
        foreach (var r in recettes)
        {
            var (cases, sources, _) = GestionAtelier.Planifier(coeur, r, false);
            // Même type pour plusieurs cases : la somme doit être disponible
            var total = new Dictionary<string, long>();
            CraftingRecipeIngredient? manque = null;
            for (int i = 0; i < 9 && manque == null; i++)
            {
                if (cases[i] is not CraftingRecipeIngredient ing) continue;
                if (sources[i] is not { } s) { manque = ing; break; }
                total[s.cle] = total.GetValueOrDefault(s.cle) + Besoin(ing);
                if (total[s.cle] > s.quantite) manque = ing;
            }
            if (manque != null) { premierManque ??= manque; continue; }
            int n = Executer(coeur, r, cases, sources);
            if (n > 0) return (n, null);
        }
        return (0, premierManque);
    }

    private static int Executer(BECoeur coeur, GridRecipe r, CraftingRecipeIngredient?[] cases, (string cle, ItemStack exemple, long quantite)?[] sources)
    {
        var world = coeur.Api.World;
        var inventaire = new DummyInventory(coeur.Api, 9);
        var slots = Enumerable.Range(0, 9).Select(i => (ItemSlot)inventaire[i]).ToArray();
        void Rendre() { foreach (var s in slots) if (!s.Empty) { coeur.Restituer(s.Itemstack); s.Itemstack = null; } }
        for (int i = 0; i < 9; i++)
        {
            if (cases[i] is not CraftingRecipeIngredient ing || sources[i] is not { } src) continue;
            int q = Besoin(ing);
            var pile = coeur.Extraire(src.cle, q);
            if (pile == null || pile.StackSize < q) { if (pile != null) coeur.Restituer(pile); Rendre(); return 0; }
            slots[i].Itemstack = pile;
        }
        var sortie = new DummySlot();
        try { r.GenerateOutputStack(slots, sortie); }
        catch (Exception e) { coeur.Api.Logger.Warning("[curveostockage] automate : recette {0} : {1}", r.Name, e.Message); Rendre(); return 0; }
        if (sortie.Empty) { Rendre(); return 0; }

        // Consommation (comme la grille du jeu, sans joueur : outils usés ici, objets rendus au réseau)
        for (int i = 0; i < 9; i++)
        {
            if (cases[i] is not CraftingRecipeIngredient ing || slots[i].Empty) continue;
            var slot = slots[i];
            var cp = ing.ConsumeProperties;
            if (!cp.Consume)
            {
                int cout = Math.Max(cp.DurabilityCost, -cp.DurabilityChange);
                if (cout > 0 && slot.Itemstack!.Collectible.GetMaxDurability(slot.Itemstack) > 0)
                {
                    int reste = slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) - cout;
                    if (reste <= 0 && cp.BreakOnZeroDurability) slot.Itemstack = null;
                    else slot.Itemstack.Collectible.SetDurability(slot.Itemstack, Math.Max(0, reste));
                }
                continue;
            }
            slot.Itemstack!.StackSize -= ing.Quantity;
            if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            if (ing.ReturnedStack?.ResolvedItemstack is ItemStack rendu) coeur.Restituer(rendu.Clone());
        }
        Rendre();
        int n = sortie.Itemstack.StackSize;
        coeur.Restituer(sortie.Itemstack);
        return n;
    }

    /// <summary>
    /// Quantité de cet objet dans le réseau, tous exemplaires du même objet confondus : la fabrication ajoute parfois
    /// des attributs (la clé change), le stock doit pourtant compter ce que l'automate a déjà fait.
    /// </summary>
    public static long EnStock(BECoeur coeur, ItemStack sortie)
        => coeur.Contenu().Where(c => c.exemple.Collectible.Code.Equals(sortie.Collectible.Code)).Sum(c => c.quantite);

    /// <summary>Toutes les cartes des automates du réseau.</summary>
    public static IEnumerable<ItemStack> DuReseau(BECoeur coeur)
    {
        foreach (var pos in coeur.Automates())
            if (coeur.Api.World.BlockAccessor.GetBlockEntity(pos) is BEAutomate a)
                foreach (var c in a.CartesPosees()) yield return c;
    }

    /// <summary>Une carte du réseau dont l'objet fabriqué convient à cet ingrédient.</summary>
    public static ItemStack? QuiFabrique(BECoeur coeur, CraftingRecipeIngredient ing)
        => DuReseau(coeur).FirstOrDefault(c => ItemCarte.Sortie(coeur.Api.World, c) is ItemStack s && ing.SatisfiesAsIngredient(s, false));

    /// <summary>
    /// Ce qui manquerait pour fabriquer n fois la carte, en passant par les autres cartes du réseau pour les
    /// ingrédients fabricables (profondeur limitée). Clé : nom de l'ingrédient, valeur : quantité manquante.
    /// </summary>
    public static Dictionary<string, long> Manques(BECoeur coeur, ItemStack carte, int n)
    {
        var world = coeur.Api.World;
        var stock = coeur.Contenu().Select(c => (c.exemple, reste: c.quantite)).ToList();
        var manques = new Dictionary<string, long>();
        void Simuler(ItemStack c, long fois, int profondeur)
        {
            var r = Recettes(world, c).FirstOrDefault();
            if (r == null) { manques[Lang.Get("curveostockage:carte-inconnue")] = 1; return; }
            foreach (var g in Cases(r).OfType<CraftingRecipeIngredient>().GroupBy(i => i))
            {
                var ing = g.Key;
                long besoin = ing.ConsumeProperties.Consume ? ing.Quantity * g.Count() * fois : 1;
                for (int k = 0; k < stock.Count && besoin > 0; k++)
                {
                    if (stock[k].reste <= 0 || !ing.SatisfiesAsIngredient(stock[k].exemple, false)) continue;
                    long pris = Math.Min(besoin, stock[k].reste);
                    if (ing.ConsumeProperties.Consume) stock[k] = (stock[k].exemple, stock[k].reste - pris);
                    besoin -= pris;
                }
                if (besoin <= 0) continue;
                var sous = profondeur < ProfondeurMax ? QuiFabrique(coeur, ing) : null;
                int par = sous != null ? ItemCarte.Sortie(world, sous)?.StackSize ?? 1 : 1;
                if (sous != null) Simuler(sous, (besoin + par - 1) / par, profondeur + 1);
                else
                {
                    string nom = ing.ResolvedItemStack?.GetName() ?? ing.Code?.ToString() ?? "?";
                    manques[nom] = manques.GetValueOrDefault(nom) + besoin;
                }
            }
        }
        Simuler(carte, n, 0);
        return manques;
    }
}

/// <summary>Automate horloger : jusqu'à 9 cartes perforées, un axe mécanique au-dessus ou en dessous l'accélère.</summary>
public class BlockAutomate : BlockMPBase, IReseau
{
    public string Role => "automate";

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;
        // Façade vers le joueur, comme les autres blocs du mod
        var face = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var bloc = world.GetBlock(CodeWithVariant("orientation", face.Code)) ?? this;
        if (!bloc.DoPlaceBlock(world, byPlayer, blockSel, itemstack)) return false;
        if (bloc is BlockAutomate a && !a.tryConnect(world, byPlayer, blockSel.Position, BlockFacing.UP))
            a.tryConnect(world, byPlayer, blockSel.Position, BlockFacing.DOWN);
        return true;
    }

    public override bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock)
        => face == BlockFacing.UP || face == BlockFacing.DOWN;

    public override void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face) { }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        StockageSystem.Revision++;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        StockageSystem.Revision++;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEAutomate a) return a.OnPlayerRightClick(byPlayer, blockSel);
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    private Block VarianteObjet(IWorldAccessor world) => world.GetBlock(CodeWithVariant("orientation", "north")) ?? this;

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        => new[] { new ItemStack(VarianteObjet(world)) };

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => new(VarianteObjet(world));
}

/// <summary>
/// Automate horloger. Côté serveur : maintient le stock demandé par ses cartes et exécute les commandes du réseau,
/// une fabrication à la fois, plus vite quand un axe le fait tourner. Côté client : engrenage, tambour à picots et
/// presse animés au rythme de l'axe (ou lentement, remonté par son ressort, quand il travaille sans axe).
/// </summary>
public class BEAutomate : BlockEntityOpenableContainer
{
    public const int NbCartes = 9;
    public const int PaquetGarder = 4200, PaquetViderFile = 4201;
    private readonly InventoryGeneric inventaire;
    public override InventoryBase Inventory => inventaire;
    public override string InventoryClassName => "curveostockage-automate";

    /// <summary>A du travail (synchronisé : anime la mécanique même sans axe).</summary>
    public bool Actif;
    public string Etat = "";
    private double progres;
    private BlockPos? coeurPos;
    private int revisionCoeur = -1;
    private BEBehaviorMPConsumer? mpc;
    private RenduAutomate? rendu;
    private static ConfigStockage Cfg => StockageSystem.Config;

    public BEAutomate()
    {
        inventaire = new InventoryGeneric(NbCartes, null, null, (id, inv) => new SlotCarte(inv));
    }

    public IEnumerable<ItemStack> CartesPosees()
    {
        foreach (var s in inventaire) if (ItemCarte.Perforee(s.Itemstack)) yield return s.Itemstack!;
    }

    /// <summary>Vitesse de l'axe (0 sans axe) et multiplicateur de cadence qui en découle.</summary>
    public float VitesseAxe => Cfg.Automate.AxeActif && mpc?.Network != null ? mpc.TrueSpeed : 0f;
    public double Cadence => 1 + Cfg.Automate.BonusMecanique * Math.Min(1, VitesseAxe / Math.Max(0.01f, Cfg.Automate.VitesseAxePleine));

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        mpc = GetBehavior<BEBehaviorMPConsumer>();
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(Tick, 250);
        else if (api is ICoreClientAPI capi)
        {
            rendu = new RenduAutomate(capi, this);
            capi.Event.RegisterRenderer(rendu, EnumRenderStage.Opaque, "curveostockage-automate");
        }
    }

    private BECoeur? Coeur()
    {
        if (revisionCoeur != StockageSystem.Revision || coeurPos == null)
        {
            coeurPos = Reseau.TrouverCoeur(Api.World, Pos, out _)?.Pos.Copy();
            revisionCoeur = StockageSystem.Revision;
        }
        return coeurPos == null ? null : Api.World.BlockAccessor.GetBlockEntity(coeurPos) as BECoeur;
    }

    private int ticks;

    public void Tick(float dt)
    {
        // Fenêtre ouverte : la file et l'état sont renvoyés chaque seconde
        if (++ticks % 4 == 0 && inventaire.openedByPlayerGUIds.Count > 0) MarkDirty();
        if (!Cfg.Automate.Actif) { progres = 0; Definir(false, "curveostockage:desactive"); return; }
        var coeur = Coeur();
        if (coeur == null || coeur.Pret() != null) { Definir(false, coeur == null ? "curveostockage:erreur-sans-coeur" : coeur.Pret()!); return; }
        var cartes = CartesPosees().ToList();
        // Maintien du stock : une commande par carte en dessous de son seuil
        foreach (var c in cartes)
        {
            int garder = ItemCarte.Garder(c);
            if (garder <= 0 || coeur.Commandes.Count >= Cfg.Automate.CommandesMax || coeur.Commandes.Any(x => x.Carte == ItemCarte.Ident(c))) continue;
            if (ItemCarte.Sortie(Api.World, c) is not ItemStack sortie) continue;
            long present = Cartes.EnStock(coeur, sortie);
            if (present >= garder) continue;
            int fois = (int)((garder - present + sortie.StackSize - 1) / sortie.StackSize);
            coeur.AjouterCommande(new Commande { Carte = ItemCarte.Ident(c), Restant = fois, Total = fois, Stock = true, NomSortie = sortie.GetName() }, false);
        }
        var miennes = coeur.Commandes.Where(x => cartes.Any(c => ItemCarte.Ident(c) == x.Carte)).ToList();
        if (miennes.Count == 0) { progres = 0; Definir(false, ""); return; }
        progres += dt * 1000 / Math.Max(250, Cfg.Automate.IntervalleMs) * Cadence;
        Definir(true, Etat);
        if (progres < 1) return;
        progres = 0;
        // La plus ancienne commande qui peut avancer (les sous-commandes, ajoutées devant, passent d'abord)
        foreach (var cmd in miennes.Take(4))
        {
            var carte = cartes.First(c => ItemCarte.Ident(c) == cmd.Carte);
            var (n, manque) = CurveoStockage.Cartes.Fabriquer(coeur, carte);
            if (n > 0)
            {
                cmd.Manque = "";
                if (--cmd.Restant <= 0)
                {
                    coeur.RetirerCommande(cmd);
                    if (!cmd.Stock && cmd.Parent == 0 && ItemCarte.Sortie(Api.World, carte) is ItemStack fait)
                        coeur.Journaliser(cmd.Qui, 4, fait, cmd.Total * fait.StackSize);
                }
                coeur.Changement();
                Definir(true, "");
                return;
            }
            if (manque == null) { cmd.Manque = Lang.Get("curveostockage:carte-inconnue"); continue; }
            // Ingrédient fabricable : une sous-commande passe devant (une seule à la fois par ingrédient)
            var sous = CurveoStockage.Cartes.QuiFabrique(coeur, manque);
            if (sous != null && !coeur.Commandes.Any(x => x.Parent == cmd.Id && x.Carte == ItemCarte.Ident(sous)) && Profondeur(coeur, cmd) < CurveoStockage.Cartes.ProfondeurMax)
            {
                int par = ItemCarte.Sortie(Api.World, sous)?.StackSize ?? 1;
                long besoin = (long)(manque.ConsumeProperties.Consume ? manque.Quantity : 1) * cmd.Restant;
                int fois = (int)Math.Clamp((besoin + par - 1) / par, 1, 10_000);
                coeur.AjouterCommande(new Commande { Carte = ItemCarte.Ident(sous), Restant = fois, Total = fois, Parent = cmd.Id, Qui = cmd.Qui,
                    Stock = cmd.Stock, NomSortie = ItemCarte.Sortie(Api.World, sous)?.GetName() ?? "" }, true);
                cmd.Manque = "";
                return;
            }
            if (sous == null) cmd.Manque = manque.ResolvedItemStack?.GetName() ?? manque.Code?.ToString() ?? "?";
        }
        Definir(true, miennes.FirstOrDefault(x => x.Manque.Length > 0) is Commande bloquee ? "curveostockage:automate-manque" : "");
    }

    private static int Profondeur(BECoeur coeur, Commande cmd)
    {
        int p = 0;
        for (var c = cmd; c.Parent != 0 && p < 10; p++) c = coeur.Commandes.FirstOrDefault(x => x.Id == c.Parent) ?? new Commande();
        return p;
    }

    private void Definir(bool actif, string etat)
    {
        if (actif == Actif && etat == Etat) return;
        Actif = actif; Etat = etat;
        MarkDirty();
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            toggleInventoryDialogClient(byPlayer, () => new GuiAutomate((ICoreClientAPI)Api, this));
        return true;
    }

#pragma warning disable CS0618 // change en 1.23 (voir Stabilisateur.Autorise)
    private bool Autorise(IPlayer joueur)
        => new CachedAccessPerms(Api.World, Pos, joueur).IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, validatePickRange: true, "curveostockage");
#pragma warning restore CS0618

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        if (!Autorise(player)) return;
        if (packetid == PaquetGarder && data?.Length == 5)
        {
            // Case (1 octet) puis variation (entier)
            int i = data[0], delta = BitConverter.ToInt32(data, 1);
            if (i >= NbCartes || !ItemCarte.Perforee(inventaire[i].Itemstack)) return;
            var carte = inventaire[i].Itemstack;
            carte!.Attributes.SetInt("garder", Math.Clamp(ItemCarte.Garder(carte) + delta, 0, 100_000));
            inventaire[i].MarkDirty();
            MarkDirty();
        }
        else if (packetid == PaquetViderFile)
        {
            var coeur = Coeur();
            if (coeur == null) return;
            coeur.ViderCommandes();
            coeur.Changement();
        }
    }

    /// <summary>Les commandes du réseau, pour la fenêtre (le client n'a pas le cœur : liste envoyée avec l'entité).</summary>
    public List<Commande> File = new();

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("actif", Actif);
        tree.SetString("etat", Etat);
        if (Api?.Side == EnumAppSide.Server && Coeur() is BECoeur coeur)
            tree["file"] = new ByteArrayAttribute(SerializerUtil.Serialize(coeur.Commandes.Take(12).ToList()));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Actif = tree.GetBool("actif");
        Etat = tree.GetString("etat", "");
        if (tree["file"] is ByteArrayAttribute f && f.value?.Length > 0)
            try { File = SerializerUtil.Deserialize<List<Commande>>(f.value); } catch { File = new(); }
        else File = new();
        (invDialog as GuiAutomate)?.Maj();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (!Cfg.Automate.Actif) { dsc.AppendLine(Lang.Get("curveostockage:desactive")); return; }
        dsc.AppendLine(Lang.Get("curveostockage:automate-info", CartesPosees().Count(), NbCartes));
        dsc.AppendLine(VitesseAxe > 0.001f ? Lang.Get("curveostockage:automate-axe", Cadence.ToString("0.#")) : Lang.Get("curveostockage:automate-sans-axe"));
        if (Etat.Length > 0) dsc.AppendLine(Lang.Get(Etat));
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        rendu?.Dispose();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        rendu?.Dispose();
    }

    /// <summary>Le modèle fixe est posé ici ; les pièces mobiles sont dessinées à chaque image par <see cref="RenduAutomate"/>.</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        base.OnTesselation(mesher, tessThreadTesselator);
        tessThreadTesselator.TesselateBlock(Block, out var mesh);
        mesher.AddMeshData(mesh);
        return true;
    }
}

/// <summary>
/// Pièces mobiles de l'automate : engrenage (axe vertical, entraîné directement par l'axe mécanique), tambour à picots
/// (horizontal, démultiplié) et presse qui frappe la carte à chaque tour du tambour.
/// </summary>
public class RenduAutomate : IRenderer, IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly BEAutomate be;
    private MeshRef? engrenage, tambour, presse;
    private readonly Matrixf modele = new();
    private float angleRessort;
    private bool axeX;

    public double RenderOrder => 0.5;
    public int RenderRange => 32;

    public RenduAutomate(ICoreClientAPI capi, BEAutomate be)
    {
        this.capi = capi;
        this.be = be;
        string orientation = be.Block.Variant["orientation"] ?? "north";
        axeX = orientation is "north" or "south";
        var rotation = new Vec3f(0, orientation switch { "north" => 180, "east" => 90, "west" => 270, _ => 0 }, 0);
        MeshRef? Charger(string nom)
        {
            var forme = Shape.TryGet(capi, new AssetLocation("curveostockage", $"shapes/block/automate-{nom}.json"));
            if (forme == null) return null;
            capi.Tesselator.TesselateShape(be.Block, forme, out var mesh, rotation);
            return capi.Render.UploadMesh(mesh);
        }
        engrenage = Charger("engrenage"); tambour = Charger("tambour"); presse = Charger("presse");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (engrenage == null) return;
        var mpc = be.GetBehavior<BEBehaviorMPConsumer>();
        bool axe = mpc?.Network != null && mpc.TrueSpeed > 0.001f;
        // Entraîné par l'axe : suit exactement sa rotation ; sans axe : tourne doucement tant qu'il travaille
        if (!axe && be.Actif) angleRessort += deltaTime * 1.4f;
        float angle = axe ? mpc!.AngleRad : angleRessort;

        var rpi = capi.Render;
        var cam = capi.World.Player.Entity.CameraPos;
        var pos = be.Pos;
        rpi.GlDisableCullFace();
        var prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        void Dessiner(MeshRef? m, Action<Matrixf> mouvement)
        {
            if (m == null) return;
            modele.Identity().Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
            mouvement(modele);
            prog.ModelMatrix = modele.Values;
            rpi.RenderMesh(m);
        }
        Dessiner(engrenage, m => m.Translate(0.5f, 0, 0.5f).RotateY(angle).Translate(-0.5f, 0, -0.5f));
        float a2 = angle * 0.5f, cy = 7.5f / 16f;
        Dessiner(tambour, m =>
        {
            m.Translate(0.5f, cy, 0.5f);
            if (axeX) m.RotateX(a2); else m.RotateZ(a2);
            m.Translate(-0.5f, -cy, -0.5f);
        });
        // La presse frappe deux fois par tour du tambour
        float coup = (float)Math.Pow(Math.Max(0, Math.Sin(a2 * 2)), 6);
        Dessiner(presse, m => m.Translate(0, -coup * 0.9f / 16f, 0));
        prog.Stop();
        rpi.GlEnableCullFace();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        engrenage?.Dispose(); tambour?.Dispose(); presse?.Dispose();
        engrenage = tambour = presse = null;
    }
}

/// <summary>Perforation d'une carte (depuis le navigateur de recettes) et commandes passées depuis un terminal ou une tablette.</summary>
public static class Fabrication
{
    private static readonly HashSet<string> Reussites = new() { "curveostockage:carte-perforee-ok", "curveostockage:commande-lancee" };

    /// <summary>Réussite : message dans le chat ; échec : erreur à l'écran (fonction de la fenêtre qui a demandé).</summary>
    public static void Informer(IServerPlayer joueur, string cle, string? detail, Action<string> erreur)
    {
        string texte = Lang.GetL(joueur.LanguageCode, cle, detail ?? "");
        if (Reussites.Contains(cle)) joueur.SendMessage(GlobalConstants.GeneralChatGroup, texte, EnumChatType.Notification);
        else erreur(texte);
    }

    /// <summary>Grave la recette demandée sur une carte vierge prise dans l'inventaire du joueur ou le réseau. Renvoie la clé d'un message.</summary>
    public static string Perforer(BECoeur coeur, IServerPlayer joueur, PaquetRemplir demande)
    {
        if (!StockageSystem.Config.Automate.Actif) return "curveostockage:desactive";
        var world = coeur.Api.World;
        var recettes = world.GridRecipes;
        GridRecipe? r = demande.Index >= 0 && demande.Index < recettes.Count ? recettes[demande.Index] : null;
        bool Correspond(GridRecipe x) => (x.Name?.ToString() ?? "") == demande.Nom && PaquetRemplir.CodeSortie(x) == demande.Sortie;
        if (r == null || !Correspond(r)) r = recettes.FirstOrDefault(Correspond);
        if (r?.ResolvedIngredients == null) return "curveostockage:atelier-recette-inconnue";
        if (r.RequiresTrait != null) return "curveostockage:carte-classe";

        // Carte vierge : d'abord dans l'inventaire du joueur, sinon dans le réseau
        ItemStack? vierge = null;
        foreach (var inv in joueur.InventoryManager.InventoriesOrdered)
        {
            if (inv.ClassName == GlobalConstants.creativeInvClassName) continue;
            var slot = inv.FirstOrDefault(s => s.Itemstack?.Collectible is ItemCarte && !ItemCarte.Perforee(s.Itemstack));
            if (slot == null) continue;
            vierge = slot.TakeOut(1);
            slot.MarkDirty();
            break;
        }
        if (vierge == null)
        {
            var cle = coeur.Contenu().FirstOrDefault(c => c.exemple.Collectible is ItemCarte && !ItemCarte.Perforee(c.exemple)).cle;
            if (cle != null) vierge = coeur.Extraire(cle, 1, joueur.PlayerName);
        }
        if (vierge == null) return "curveostockage:carte-manque";
        var perforee = world.GetItem(new AssetLocation("curveostockage:carte-perforee"));
        if (perforee == null) return "curveostockage:carte-manque";
        var carte = new ItemStack(perforee);
        ItemCarte.Graver(carte, r, world.GridRecipes.IndexOf(r));
        if (!joueur.InventoryManager.TryGiveItemstack(carte, true)) world.SpawnItemEntity(carte, joueur.Entity.Pos.XYZ);
        return "curveostockage:carte-perforee-ok";
    }

    /// <summary>Commande n exemplaires d'un objet fabricable. Renvoie null si c'est lancé, sinon le message à afficher.</summary>
    public static string? Commander(BECoeur coeur, IServerPlayer joueur, PaquetCommande demande, out string? detail)
    {
        detail = null;
        if (!StockageSystem.Config.Automate.Actif) return "curveostockage:desactive";
        if (coeur.Commandes.Count >= StockageSystem.Config.Automate.CommandesMax) return "curveostockage:commande-file-pleine";
        var world = coeur.Api.World;
        var carte = Cartes.DuReseau(coeur).FirstOrDefault(c => ItemCarte.Sortie(world, c) is ItemStack s && Transitions.Cle(s) == demande.Cle);
        if (carte == null && coeur.Contenu().FirstOrDefault(c => c.cle == demande.Cle).exemple is ItemStack ex)
            carte = Cartes.DuReseau(coeur).FirstOrDefault(c => ItemCarte.Sortie(world, c)?.Collectible.Code.Equals(ex.Collectible.Code) == true);
        if (carte == null) return "curveostockage:commande-sans-carte";
        var sortie = ItemCarte.Sortie(world, carte)!;
        int fois = (demande.Quantite + sortie.StackSize - 1) / sortie.StackSize;
        var manques = Cartes.Manques(coeur, carte, fois);
        if (manques.Count > 0)
        {
            detail = string.Join(", ", manques.Take(6).Select(m => $"{m.Value} × {m.Key}"));
            return "curveostockage:commande-manque";
        }
        coeur.AjouterCommande(new Commande { Carte = ItemCarte.Ident(carte), Restant = fois, Total = fois, Qui = joueur.PlayerName, NomSortie = sortie.GetName() }, false);
        coeur.Changement();
        detail = $"{fois * sortie.StackSize} × {sortie.GetName()}";
        return null;
    }
}
