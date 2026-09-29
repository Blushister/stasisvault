using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CurveoStockage;

/// <summary>
/// Atelier (côté serveur) : la grille d'artisanat du joueur, affichée dans le terminal ou la tablette quand le réseau
/// a un bloc atelier. Après chaque fabrication, les cases vidées sont re-remplies depuis le réseau avec ce que le
/// joueur y avait posé (comme Refined Storage) ; « Vider » renvoie la grille dans le réseau.
/// </summary>
public class GestionAtelier
{
    private record Motif(string Cle, int Quantite);

    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<string, BlockPos> actifs = new();
    private readonly Dictionary<string, Motif?[]> motifs = new();
    private readonly Dictionary<string, InventoryBase> abonnes = new();
    private static readonly FieldInfo? enFabrication = FindField("isCrafting");
    public static bool FabricationDetectable => enFabrication != null;

    public GestionAtelier(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        sapi.Event.RegisterEventBusListener(Fabrique, 0.5, "onitemcrafted");
        sapi.Event.PlayerDisconnect += p => { actifs.Remove(p.PlayerUID); motifs.Remove(p.PlayerUID); abonnes.Remove(p.PlayerUID); };
    }

    private static FieldInfo? FindField(string nom)
    {
        var type = typeof(ICoreAPI).Assembly.GetType("Vintagestory.Common.InventoryCraftingGrid")
                   ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Vintagestory.Common.InventoryCraftingGrid")).FirstOrDefault(t => t != null);
        return type?.GetField(nom, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    public static InventoryBase? Grille(IPlayer joueur) => joueur.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName) as InventoryBase;

    /// <summary>Le joueur a un terminal ou une tablette ouvert sur ce réseau, qui a un atelier.</summary>
    public void Activer(IServerPlayer joueur, BlockPos coeur)
    {
        actifs[joueur.PlayerUID] = coeur.Copy();
        var grille = Grille(joueur);
        if (grille == null || (abonnes.TryGetValue(joueur.PlayerUID, out var deja) && deja == grille)) return;
        abonnes[joueur.PlayerUID] = grille;
        motifs[joueur.PlayerUID] = new Motif?[9];
        string uid = joueur.PlayerUID;
        grille.SlotModified += id => Modifie(uid, grille, id);
        for (int i = 0; i < 9 && i < grille.Count; i++) Modifie(uid, grille, i);
    }

    public void Desactiver(string uid) => actifs.Remove(uid);

    /// <summary>Retient ce que le joueur pose lui-même dans la grille (pas ce que la fabrication consomme).</summary>
    private void Modifie(string uid, InventoryBase grille, int id)
    {
        if (id < 0 || id >= 9 || !motifs.TryGetValue(uid, out var m)) return;
        if (enFabrication?.GetValue(grille) is true) return;
        var pile = grille[id].Itemstack;
        m[id] = pile == null ? null : new Motif(Transitions.Cle(pile), pile.StackSize);
    }

    private void Fabrique(string evenement, ref EnumHandling handling, IAttribute donnees)
    {
        if (!StockageSystem.Config.Atelier.RemplissageAuto) return;
        if (donnees is not ITreeAttribute arbre) return;
        long entite = arbre.GetLong("byentityid");
        var joueur = sapi.World.AllOnlinePlayers.OfType<IServerPlayer>().FirstOrDefault(p => p.Entity?.EntityId == entite);
        if (joueur == null || !actifs.ContainsKey(joueur.PlayerUID)) return;
        // Après la fin de la fabrication en cours
        sapi.Event.EnqueueMainThreadTask(() => Remplir(joueur), "curveostockage-atelier");
    }

    private void Remplir(IServerPlayer joueur)
    {
        if (!actifs.TryGetValue(joueur.PlayerUID, out var pos) || !motifs.TryGetValue(joueur.PlayerUID, out var m)) return;
        if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BECoeur coeur || !coeur.AAtelier()) return;
        var grille = Grille(joueur);
        if (grille == null) return;
        for (int i = 0; i < 9 && i < grille.Count; i++)
        {
            if (m[i] is not Motif motif || !grille[i].Empty) continue;
            var pile = coeur.Extraire(motif.Cle, motif.Quantite, joueur.PlayerName);
            if (pile == null) continue;
            grille[i].Itemstack = pile;
            grille[i].MarkDirty();
            m[i] = motif; // la case re-remplie garde son motif
        }
    }

    /// <summary>
    /// Remplit la grille avec les ingrédients d'une recette pris dans le réseau (la grille est d'abord vidée dans le réseau).
    /// max : autant de fabrications que le stock et la taille des piles le permettent. Renvoie la clé d'un message, ou null.
    /// </summary>
    public string? RemplirRecette(BECoeur coeur, IServerPlayer joueur, PaquetRemplir demande)
    {
        if (!coeur.AAtelier()) return "curveostockage:atelier-absent";
        var recettes = sapi.World.GridRecipes;
        GridRecipe? r = demande.Index >= 0 && demande.Index < recettes.Count ? recettes[demande.Index] : null;
        bool Correspond(GridRecipe x) => (x.Name?.ToString() ?? "") == demande.Nom && PaquetRemplir.CodeSortie(x) == demande.Sortie;
        if (r == null || !Correspond(r)) r = recettes.FirstOrDefault(Correspond);
        if (r?.ResolvedIngredients == null) return "curveostockage:atelier-recette-inconnue";

        var grille = Grille(joueur);
        if (grille == null) return null;
        Vider(coeur, joueur, false);
        if (Enumerable.Range(0, 9).Any(i => !grille[i].Empty)) return "curveostockage:atelier-grille-pleine";

        var (cases, sources, n) = Planifier(coeur, r, demande.Max);
        bool manque = false;
        for (int i = 0; i < 9; i++)
        {
            if (cases[i] is not CraftingRecipeIngredient ing) continue;
            if (sources[i] is not { } s) { manque = true; continue; }
            int q = ing.IsTool ? 1 : ing.Quantity * n;
            var pile = coeur.Extraire(s.cle, q, joueur.PlayerName);
            if (pile == null) { manque = true; continue; }
            if (pile.StackSize < q) manque = true;
            grille[i].Itemstack = pile;
            grille[i].MarkDirty();
        }
        return manque ? "curveostockage:atelier-manque" : null;
    }

    /// <summary>Pour chaque case : l'ingrédient, le type du réseau qui le fournit, et le nombre de fabrications possibles.</summary>
    public static (CraftingRecipeIngredient?[] cases, (string cle, ItemStack exemple, long quantite)?[] sources, int n) Planifier(BECoeur coeur, GridRecipe r, bool max)
    {
        // Cases de la grille (comme la recette ; sans forme : à la suite)
        var cases = new CraftingRecipeIngredient?[9];
        var ings = r.ResolvedIngredients!;
        if (r.Shapeless) { int k = 0; foreach (var i in ings) if (i != null && k < 9) cases[k++] = i; }
        else for (int y = 0; y < r.Height && y < 3; y++) for (int x = 0; x < r.Width && x < 3; x++) cases[y * 3 + x] = ings[y * r.Width + x];

        // Une source par ingrédient (le type le plus abondant qui convient), la même pour les cases identiques
        var contenu = coeur.Contenu();
        var sources = new (string cle, ItemStack exemple, long quantite)?[9];
        var besoin = new Dictionary<string, long>();
        for (int i = 0; i < 9; i++)
        {
            if (cases[i] is not CraftingRecipeIngredient ing) continue;
            var deja = Enumerable.Range(0, i).FirstOrDefault(j => cases[j] != null && ReferenceEquals(cases[j], ing) && sources[j] != null, -1);
            var src = deja >= 0 ? sources[deja]
                : contenu.Where(c => ing.SatisfiesAsIngredient(c.exemple, false)).OrderByDescending(c => c.quantite).Cast<(string cle, ItemStack exemple, long quantite)?>().FirstOrDefault();
            sources[i] = src;
            if (src is { } s) besoin[s.cle] = besoin.GetValueOrDefault(s.cle) + (ing.IsTool ? 0 : ing.Quantity);
        }

        int n = 1;
        if (max)
        {
            n = int.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                if (cases[i] is not CraftingRecipeIngredient ing || ing.IsTool || sources[i] is not { } s) continue;
                n = (int)Math.Min(n, s.quantite / Math.Max(1, besoin[s.cle]));
                n = Math.Min(n, Math.Max(1, s.exemple.Collectible.MaxStackSize / Math.Max(1, ing.Quantity)));
            }
            if (n == int.MaxValue || n < 1) n = 1;
        }
        return (cases, sources, n);
    }

    /// <summary>
    /// Renvoie la grille dans le réseau. En fermant, ce qui ne rentre pas (ou s'il n'y a plus de réseau) va dans
    /// l'inventaire du joueur, sinon à ses pieds ; sans fermer, le reste reste dans la grille.
    /// </summary>
    public void Vider(BECoeur? coeur, IServerPlayer joueur, bool fermer)
    {
        var grille = Grille(joueur);
        if (grille == null) return;
        bool reseau = coeur != null && coeur.AAtelier();
        for (int i = 0; i < 9 && i < grille.Count; i++)
        {
            var slot = grille[i];
            if (slot.Empty) continue;
            var pile = slot.Itemstack;
            if (reseau) coeur!.Inserer(pile, out _, joueur.PlayerName);
            if (pile.StackSize > 0 && fermer)
            {
                joueur.InventoryManager.TryGiveItemstack(pile, true);
                if (pile.StackSize > 0) sapi.World.SpawnItemEntity(pile, joueur.Entity.Pos.XYZ);
                pile.StackSize = 0;
            }
            if (pile.StackSize <= 0) slot.Itemstack = null;
            slot.MarkDirty();
        }
        if (motifs.TryGetValue(joueur.PlayerUID, out var m)) Array.Clear(m);
        if (fermer) Desactiver(joueur.PlayerUID);
    }
}
