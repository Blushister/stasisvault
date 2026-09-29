using Cairo;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>Demande de remplissage de la grille d'artisanat avec une recette (index dans World.GridRecipes, vérifié par le nom et la sortie).</summary>
[ProtoContract]
public class PaquetRemplir
{
    [ProtoMember(1)] public int Index;
    [ProtoMember(2)] public string Nom = "";
    [ProtoMember(3)] public string Sortie = "";
    [ProtoMember(4)] public bool Max;

    public static PaquetRemplir? Lire(byte[]? data)
    {
        if (data == null) return null;
        try
        {
            var p = Vintagestory.API.Util.SerializerUtil.Deserialize<PaquetRemplir>(data);
            return p != null && p.Nom.Length < 256 && p.Sortie.Length < 256 ? p : null;
        }
        catch { return null; }
    }

    public static string CodeSortie(GridRecipe r) => (r.Output?.ResolvedItemStack?.Collectible?.Code?.ToString()) ?? "";
}

/// <summary>
/// Index des recettes de la grille, construit une fois par session : une entrée par objet fabriqué, avec ses recettes.
/// Les noms sont calculés à la première recherche ; « faisable » est recalculé seulement quand le contenu du réseau change.
/// </summary>
public class IndexRecettes
{
    public class Sortie
    {
        public ItemStack Pile = null!;
        public readonly List<(GridRecipe recette, int index)> Recettes = new();
        public string? Nom;
        public bool Faisable;
        private DummySlot? slot;
        public DummySlot Slot => slot ??= new DummySlot(Pile);
    }

    private static IndexRecettes? instance;
    private static IWorldAccessor? monde;
    public readonly List<Sortie> Sorties = new();
    private string empreinteStock = "";
    private readonly Dictionary<string, bool> jokers = new();

    /// <summary>Un index par monde : en changeant de serveur sans relancer le jeu, les recettes sont relues.</summary>
    public static IndexRecettes De(ICoreClientAPI capi)
    {
        if (instance == null || monde != capi.World) { instance = new IndexRecettes(capi); monde = capi.World; }
        return instance;
    }

    private IndexRecettes(ICoreClientAPI capi)
    {
        var parCode = new Dictionary<string, Sortie>();
        var recettes = capi.World.GridRecipes;
        for (int i = 0; i < recettes.Count; i++)
        {
            var r = recettes[i];
            var pile = r.Output?.ResolvedItemStack;
            if (!r.Enabled || pile?.Collectible == null || r.ResolvedIngredients == null) continue;
            string cle = pile.Class + ":" + pile.Collectible.Code;
            if (!parCode.TryGetValue(cle, out var s))
            {
                s = new Sortie { Pile = pile.Clone() };
                s.Pile.StackSize = 1;
                parCode[cle] = s;
                Sorties.Add(s);
            }
            s.Recettes.Add((r, i));
        }
    }

    public string NomDe(Sortie s) => s.Nom ??= (s.Pile.GetName() ?? "").ToLowerInvariant();

    private static string Signature(CraftingRecipeIngredient ing)
        => $"{ing.Type}|{ing.Code}|{ing.MatchingType}|{string.Join(",", ing.AllowedVariants ?? Array.Empty<string>())}|{string.Join(",", ing.SkipVariants ?? Array.Empty<string>())}";

    /// <summary>L'ingrédient est présent dans le stock (approché : même objet, sans regarder la quantité).</summary>
    public bool Disponible(CraftingRecipeIngredient? ing, IReadOnlyList<ItemStack> stock, HashSet<string> codes)
    {
        if (ing == null) return true;
        if (ing.MatchingType == EnumRecipeMatchType.Exact) return ing.ResolvedItemStack?.Collectible is CollectibleObject c && codes.Contains(ing.Type + ":" + c.Code);
        string sig = Signature(ing);
        if (!jokers.TryGetValue(sig, out var ok)) jokers[sig] = ok = stock.Any(s => ing.SatisfiesAsIngredient(s, false));
        return ok;
    }

    public static HashSet<string> Codes(IReadOnlyList<ItemStack> stock) => stock.Select(s => s.Class + ":" + s.Collectible.Code).ToHashSet();

    /// <summary>Recalcule « faisable avec le stock » si le contenu du réseau a changé.</summary>
    public void MajFaisable(IReadOnlyList<ItemStack> stock)
    {
        var codes = Codes(stock);
        string empreinte = string.Join(";", codes.OrderBy(c => c, StringComparer.Ordinal));
        if (empreinte == empreinteStock) return;
        empreinteStock = empreinte;
        jokers.Clear();
        foreach (var s in Sorties)
            s.Faisable = s.Recettes.Any(r => r.recette.ResolvedIngredients!.All(i => Disponible(i, stock, codes)));
    }
}

/// <summary>Grille d'icônes des objets fabricables : seules les cases visibles sont dessinées ; molette pour défiler.</summary>
public class ListeRecettes : ElementDynamique
{
    private List<IndexRecettes.Sortie> sorties = new();
    private readonly Action<IndexRecettes.Sortie> choix;
    private IndexRecettes.Sortie? choisie;
    private int debut, survol = -1;
    private LoadedTexture? bulle;
    private IndexRecettes.Sortie? bulleDe;
    private double Cellule => scaled(42);
    private int Colonnes => Math.Max(1, (int)((Bounds.InnerWidth - scaled(14)) / Cellule));
    private int Lignes => Math.Max(1, (int)((Bounds.InnerHeight - scaled(8)) / Cellule));

    public ListeRecettes(ICoreClientAPI capi, ElementBounds bounds, Action<IndexRecettes.Sortie> choix) : base(capi, bounds) { this.choix = choix; }

    public void Definir(List<IndexRecettes.Sortie> liste, IndexRecettes.Sortie? choisie)
    {
        sorties = liste; this.choisie = choisie;
        debut = Math.Clamp(debut, 0, MaxDebut());
        Rafraichir();
    }

    private int LignesTotales => (sorties.Count + Colonnes - 1) / Colonnes;
    private int MaxDebut() => Math.Max(0, LignesTotales - Lignes);

    private (double x, double y) Case(int rang)
    {
        int r = rang / Colonnes - debut, c = rang % Colonnes;
        return (scaled(4) + c * Cellule, scaled(4) + r * Cellule);
    }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(8), Style.Creux, Style.Trait);
        int premier = debut * Colonnes, dernier = Math.Min(sorties.Count, premier + Lignes * Colonnes);
        for (int i = premier; i < dernier; i++)
        {
            var (x, y) = Case(i);
            bool actif = sorties[i] == choisie, sur = i == survol;
            Style.Bloc(ctx, x + scaled(2), y + scaled(2), Cellule - scaled(4), Cellule - scaled(4), scaled(5),
                actif ? Style.Alpha(Style.Lueur, 0.16) : sur ? Style.Surface2 : Style.Surface,
                actif ? Style.Lueur : sur ? Style.TraitFort : Style.Trait);
            if (sorties[i].Faisable)
            {
                // Coin vert : fabricable avec le stock du réseau
                ctx.MoveTo(x + Cellule - scaled(3), y + scaled(3)); ctx.LineTo(x + Cellule - scaled(13), y + scaled(3)); ctx.LineTo(x + Cellule - scaled(3), y + scaled(13)); ctx.ClosePath();
                Style.Couleur(ctx, Style.Ok); ctx.Fill();
            }
        }
        if (sorties.Count == 0)
        {
            var f = Style.Police(13.5, Style.Texte3);
            string t = Lang.Get("curveostockage:recettes-aucune");
            Style.Ecrire(api, ctx, f, t, (l - Style.Largeur(f, t)) / 2, h / 2 - scaled(10));
        }
        // Barre de défilement fine
        if (LignesTotales > Lignes)
        {
            double hb = h - scaled(12), part = (double)Lignes / LignesTotales, pos = (double)debut / Math.Max(1, MaxDebut());
            Style.Bloc(ctx, l - scaled(8), scaled(6), scaled(4), hb, scaled(2), Style.Surface);
            Style.Bloc(ctx, l - scaled(8), scaled(6) + (hb - hb * part) * pos, scaled(4), Math.Max(scaled(12), hb * part), scaled(2), Style.TraitFort);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        int premier = debut * Colonnes, dernier = Math.Min(sorties.Count, premier + Lignes * Colonnes);
        // Certains objets (arcs, bâtons, lances…) ont une grande icône : chacune est coupée au bord de sa case
        for (int i = premier; i < dernier; i++)
        {
            var (x, y) = Case(i);
            double cx = Bounds.renderX + x, cy = Bounds.renderY + y;
            Decouper(api, cx + scaled(2), cy + scaled(2), Cellule - scaled(4), Cellule - scaled(4));
            api.Render.RenderItemstackToGui(sorties[i].Slot, cx + Cellule / 2, cy + Cellule / 2, 100, (float)scaled(22), -1, showStackSize: false);
        }
        api.Render.GlScissorFlag(false);
        if (survol >= 0 && survol < sorties.Count && IsPositionInside(api.Input.MouseX, api.Input.MouseY)) Bulle(sorties[survol]);
    }

    /// <summary>Petite infobulle : nom et mod de l'objet survolé.</summary>
    private void Bulle(IndexRecettes.Sortie s)
    {
        if (bulleDe != s || bulle == null)
        {
            bulleDe = s;
            var fn = Style.Police(14, Style.LaitonClair, gras: true);
            var fm = Style.Police(12, Style.Texte3);
            string nom = s.Pile.GetName(), dom = s.Pile.Collectible.Code.Domain;
            string mod = dom == "game" ? "Vintage Story" : api.ModLoader.GetMod(dom)?.Info?.Name ?? dom;
            int l = (int)(Math.Max(Style.Largeur(fn, nom), Style.Largeur(fm, mod)) + scaled(20)), h = (int)scaled(44);
            using var surf = new ImageSurface(Format.Argb32, l, h);
            using var ctx = new Context(surf);
            Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(6), Style.Alpha(Style.Panneau, 0.97), Style.TraitFort);
            Style.Ecrire(api, ctx, fn, nom, scaled(10), scaled(5));
            Style.Ecrire(api, ctx, fm, mod, scaled(10), scaled(24));
            bulle ??= new LoadedTexture(api);
            api.Gui.LoadOrUpdateCairoTexture(surf, true, ref bulle);
        }
        double x = api.Input.MouseX + scaled(16), y = api.Input.MouseY + scaled(12);
        if (x + bulle.Width > api.Render.FrameWidth) x = api.Input.MouseX - bulle.Width - scaled(8);
        api.Render.Render2DTexturePremultipliedAlpha(bulle.TextureId, (float)x, (float)y, bulle.Width, bulle.Height, 1000);
    }

    /// <summary>Limite le dessin à un rectangle de l'écran (coordonnées d'interface, origine en haut à gauche).</summary>
    public static void Decouper(ICoreClientAPI api, double x, double y, double l, double h)
    {
        api.Render.GlScissorFlag(true);
        api.Render.GlScissor((int)x, (int)(api.Render.FrameHeight - y - h), (int)l, (int)h);
    }

    private int Index(MouseEvent args)
    {
        var (x, y) = Relatif(args);
        int c = (int)((x - scaled(4)) / Cellule), r = (int)((y - scaled(4)) / Cellule);
        if (c < 0 || c >= Colonnes || r < 0 || r >= Lignes) return -1;
        int i = (debut + r) * Colonnes + c;
        return i < sorties.Count ? i : -1;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseMove(api, args);
        int s = IsPositionInside(args.X, args.Y) ? Index(args) : -1;
        if (s != survol) { survol = s; Rafraichir(); }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        int i = Index(args);
        if (i < 0) return;
        choisie = sorties[i];
        api.Gui.PlaySound("menubutton_press");
        choix(choisie);
        Rafraichir();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY)) return;
        int nouveau = Math.Clamp(debut - Math.Sign(args.delta), 0, MaxDebut());
        if (nouveau != debut) { debut = nouveau; Rafraichir(); }
        args.SetHandled(true);
    }

    public override void Dispose() { base.Dispose(); bulle?.Dispose(); }
}

/// <summary>
/// Aperçu de la recette choisie : grille 3×3 des ingrédients (les jokers défilent parmi leurs variantes, le stock en
/// premier), manquants en rouge, résultat à droite, ◀ ▶ entre les recettes du même objet.
/// </summary>
public class ApercuRecette : ElementDynamique
{
    private IndexRecettes.Sortie? sortie;
    private int rang;
    private IReadOnlyList<ItemStack> stock = Array.Empty<ItemStack>();
    private HashSet<string> codes = new();
    private readonly Dictionary<CraftingRecipeIngredient, List<ItemStack>> variantes = new(ReferenceEqualityComparer.Instance);
    private (double x, double y, double l, double h) zonePrec, zoneSuiv;
    private double Cellule => scaled(34);

    public ApercuRecette(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public (GridRecipe recette, int index)? Recette => sortie == null || sortie.Recettes.Count == 0 ? null : sortie.Recettes[Math.Clamp(rang, 0, sortie.Recettes.Count - 1)];

    public void Definir(IndexRecettes.Sortie? s, IReadOnlyList<ItemStack> stock)
    {
        if (s != sortie) { rang = 0; variantes.Clear(); }
        sortie = s; this.stock = stock; codes = IndexRecettes.Codes(stock);
        Rafraichir();
    }

    private List<ItemStack> Variantes(CraftingRecipeIngredient ing)
    {
        if (variantes.TryGetValue(ing, out var v)) return v;
        v = new List<ItemStack>();
        if (ing.MatchingType == EnumRecipeMatchType.Exact) { if (ing.ResolvedItemStack != null) v.Add(ing.ResolvedItemStack); }
        else
        {
            v.AddRange(stock.Where(s => ing.SatisfiesAsIngredient(s, false)));
            if (v.Count == 0)
                foreach (var c in api.World.Collectibles)
                {
                    if (c?.Code == null || c.IsMissing) continue;
                    var p = new ItemStack(c);
                    if (ing.SatisfiesAsIngredient(p, false)) v.Add(p);
                    if (v.Count >= 24) break;
                }
        }
        return variantes[ing] = v;
    }

    private (double x, double y) Origine => (scaled(4), scaled(30));

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(8), Style.Surface, Style.Trait);
        zonePrec = zoneSuiv = default;
        if (Recette is not (GridRecipe r, _) || sortie == null)
        {
            var f = Style.Police(13.5, Style.Texte3);
            string t = Lang.Get("curveostockage:recettes-choisir");
            Style.Ecrire(api, ctx, f, t, (l - Style.Largeur(f, t)) / 2, h / 2 - scaled(10));
            return;
        }
        // En-tête : nom + choix de la recette
        var fn = Style.Police(14, Style.LaitonClair, gras: true);
        string nom = sortie.Pile.GetName();
        int n = sortie.Recettes.Count;
        double lNav = 0;
        if (n > 1)
        {
            var fv = Style.Police(13, Style.Texte2, gras: true);
            string t = $"{rang + 1}/{n}";
            double lt = Style.Largeur(fv, t), hb = scaled(20);
            lNav = lt + 2 * hb + scaled(12);
            double x0 = l - scaled(8) - lNav;
            zonePrec = (x0, scaled(5), hb, hb);
            zoneSuiv = (l - scaled(8) - hb, scaled(5), hb, hb);
            foreach (var (z, g) in new[] { (zonePrec, true), (zoneSuiv, false) })
            {
                Style.Bloc(ctx, z.x, z.y, z.l, z.h, scaled(4), Style.Creux, Style.TraitFort);
                Style.Couleur(ctx, Style.Texte); double cx = z.x + z.l / 2, cy = z.y + z.h / 2, d = scaled(4);
                ctx.MoveTo(cx + (g ? d / 2 : -d / 2), cy - d); ctx.LineTo(cx + (g ? -d : d), cy); ctx.LineTo(cx + (g ? d / 2 : -d / 2), cy + d); ctx.ClosePath(); ctx.Fill();
            }
            Style.Ecrire(api, ctx, fv, t, x0 + hb + scaled(6), scaled(6));
        }
        while (nom.Length > 4 && Style.Largeur(fn, nom) > l - scaled(20) - lNav) nom = nom[..^2] + "…";
        Style.Ecrire(api, ctx, fn, nom, scaled(10), scaled(6));

        // Grille 3×3 (ingrédients placés comme dans la recette ; sans forme : à la suite)
        var (ox, oy) = Origine;
        ox += scaled(6);
        var cases = Cases(r);
        for (int i = 0; i < 9; i++)
        {
            double x = ox + (i % 3) * Cellule, y = oy + (i / 3) * Cellule;
            var ing = cases[i];
            bool ok = ing == null || IndexRecettesDisponible(ing);
            Style.Bloc(ctx, x + scaled(1), y + scaled(1), Cellule - scaled(2), Cellule - scaled(2), scaled(4),
                ing == null ? Style.Creux : ok ? Style.Surface2 : Style.Alpha(Style.Bas, 0.18), ing == null ? Style.Trait : ok ? Style.TraitFort : Style.Bas);
        }
        // Flèche et résultat
        double xf = ox + 3 * Cellule + scaled(8), yc = oy + 1.5 * Cellule;
        Style.Couleur(ctx, Style.Laiton); ctx.LineWidth = scaled(2.5);
        ctx.MoveTo(xf, yc); ctx.LineTo(xf + scaled(20), yc); ctx.Stroke();
        ctx.MoveTo(xf + scaled(14), yc - scaled(6)); ctx.LineTo(xf + scaled(20), yc); ctx.LineTo(xf + scaled(14), yc + scaled(6)); ctx.Stroke();
        double xr = xf + scaled(28);
        Style.Bloc(ctx, xr, yc - scaled(22), scaled(44), scaled(44), scaled(6), Style.Alpha(Style.Lueur, 0.12), Style.Lueur);
        int q = r.Output?.Quantity ?? 1;
        if (q > 1) { var fq = Style.Police(13, Style.Texte, gras: true); Style.Ecrire(api, ctx, fq, "×" + q, xr, yc + scaled(24)); }
    }

    private bool IndexRecettesDisponible(CraftingRecipeIngredient ing) => IndexRecettes.De(api).Disponible(ing, stock, codes);

    private static CraftingRecipeIngredient?[] Cases(GridRecipe r)
    {
        var cases = new CraftingRecipeIngredient?[9];
        var ings = r.ResolvedIngredients ?? Array.Empty<CraftingRecipeIngredient?>();
        if (r.Shapeless) { int k = 0; foreach (var i in ings) if (i != null && k < 9) cases[k++] = i; }
        else for (int y = 0; y < r.Height && y < 3; y++) for (int x = 0; x < r.Width && x < 3; x++) cases[y * 3 + x] = ings[y * r.Width + x];
        return cases;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        if (Recette is not (GridRecipe r, _)) return;
        var (ox, oy) = Origine;
        ox += scaled(6);
        var cases = Cases(r);
        long seconde = api.World.ElapsedMilliseconds / 1000;
        for (int i = 0; i < 9; i++)
        {
            if (cases[i] is not CraftingRecipeIngredient ing) continue;
            var v = Variantes(ing);
            if (v.Count == 0) continue;
            var p = v[(int)(seconde % v.Count)];
            ListeRecettes.Decouper(api, Bounds.renderX + ox + (i % 3) * Cellule + scaled(2), Bounds.renderY + oy + (i / 3) * Cellule + scaled(2), Cellule - scaled(4), Cellule - scaled(4));
            api.Render.RenderItemstackToGui(new DummySlot(p), Bounds.renderX + ox + (i % 3) * Cellule + Cellule / 2, Bounds.renderY + oy + (i / 3) * Cellule + Cellule / 2, 100, (float)scaled(20), -1, showStackSize: false);
        }
        if (r.Output?.ResolvedItemStack is ItemStack sortieR)
        {
            double xr = ox + 3 * Cellule + scaled(36), yc = oy + 1.5 * Cellule;
            ListeRecettes.Decouper(api, Bounds.renderX + xr, Bounds.renderY + yc - scaled(22), scaled(44), scaled(44));
            api.Render.RenderItemstackToGui(new DummySlot(sortieR), Bounds.renderX + xr + scaled(22), Bounds.renderY + yc, 100, (float)scaled(26), -1, showStackSize: false);
        }
        api.Render.GlScissorFlag(false);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        if (sortie == null || sortie.Recettes.Count < 2) return;
        var (x, y) = Relatif(args);
        bool Dans((double x, double y, double l, double h) z) => x >= z.x && x < z.x + z.l && y >= z.y && y < z.y + z.h;
        int n = sortie.Recettes.Count;
        if (Dans(zonePrec)) rang = (rang + n - 1) % n;
        else if (Dans(zoneSuiv)) rang = (rang + 1) % n;
        else return;
        api.Gui.PlaySound("menubutton_press");
        Rafraichir();
    }
}

/// <summary>
/// Fenêtre des recettes (façon JEI), collée au bord droit de l'écran sur toute la hauteur. Ouverte avec le terminal ou la
/// tablette quand le réseau a un atelier ; la recherche, le filtre et l'objet choisi sont gardés d'une ouverture à l'autre.
/// </summary>
public class GuiRecettes : GuiDialog
{
    private System.Func<List<ItemStack>> stock;
    private Action<int, byte[]?> envoyer;
    private string recherche = "";
    private bool avecStock;
    private IndexRecettes.Sortie? choisie;
    private static GuiRecettes? instance;

    /// <summary>
    /// Une seule fenêtre par session, réutilisée par tous les terminaux et tablettes : elle garde la recherche, le
    /// filtre et l'objet choisi, et ne s'accumule pas dans la liste des fenêtres du jeu.
    /// </summary>
    public static GuiRecettes Pour(ICoreClientAPI capi, System.Func<List<ItemStack>> stock, Action<int, byte[]?> envoyer)
    {
        if (instance == null || instance.capi != capi) instance = new GuiRecettes(capi, stock, envoyer);
        instance.stock = stock; instance.envoyer = envoyer;
        return instance;
    }

    public override string? ToggleKeyCombinationCode => null;
    public override bool PrefersUngrabbedMouse => true;

    private GuiRecettes(ICoreClientAPI capi, System.Func<List<ItemStack>> stock, Action<int, byte[]?> envoyer) : base(capi)
    {
        this.stock = stock; this.envoyer = envoyer;
    }

    private void Composer()
    {
        const double L = 300;
        double echelle = Math.Max(0.5, GuiElement.scaled(1));
        // Toute la hauteur de l'écran (en unités d'interface), moins le cadre et la barre de titre
        double H = Math.Max(420, capi.Render.FrameHeight / echelle - 2 * GuiStyle.DialogToScreenPadding - 2 * (GuiStyle.ElementToDialogPadding + 6));
        double y = 38;
        var rechB = ElementBounds.Fixed(0, y, L, 30); y += 36;
        var pucesB = ElementBounds.Fixed(0, y, L, 28); y += 36;
        double hBas = 146 + 8 + 48;
        var listeB = ElementBounds.Fixed(0, y, L, Math.Max(90, H - y - hBas - 8));
        var apercuB = ElementBounds.Fixed(0, H - hBas, L, 146);
        var remplirB = ElementBounds.Fixed(0, H - 48, (L - 8) / 2, 48);
        var perforerB = ElementBounds.Fixed((L + 8) / 2, H - 48, (L - 8) / 2, 48);
        var fond = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding + 6);
        fond.BothSizing = ElementSizing.FitToChildren;
        fond.WithChildren(rechB, pucesB, listeB, apercuB, remplirB, perforerB);
        var dialogue = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        Positions.Oublier(capi, "curveostockage-recettes");
        var c = capi.Gui.CreateCompo("curveostockage-recettes", dialogue);
        c.AddStaticElement(new CadreStasis(capi, fond));
        c.AddInteractiveElement(new TitreStasis(capi, Lang.Get("curveostockage:section-recettes"), c, () => TryClose()), "titre");
        c.BeginChildElements(fond);
        c.AddTextInput(rechB, t => { recherche = t ?? ""; Filtrer(); }, Style.Police(14, Style.Texte), "recherche");
        c.AddInteractiveElement(new PucesStasis(capi, pucesB, avecStock ? "stock" : "toutes", id => { avecStock = id == "stock"; Filtrer(); }), "puces");
        c.AddInteractiveElement(new ListeRecettes(capi, listeB, s => { choisie = s; MajApercu(); }), "liste");
        c.AddInteractiveElement(new ApercuRecette(capi, apercuB), "apercu");
        c.AddInteractiveElement(new BoutonMode(capi, remplirB, Lang.Get("curveostockage:recettes-remplir"), Lang.Get("curveostockage:recettes-remplir-effet"), Remplir), "remplir");
        c.AddInteractiveElement(new BoutonMode(capi, perforerB, Lang.Get("curveostockage:recettes-perforer"), Lang.Get("curveostockage:recettes-perforer-effet"), Perforer), "perforer");
        c.EndChildElements();
        SingleComposer = c.Compose();
        var champ = SingleComposer.GetTextInput("recherche");
        champ.SetPlaceHolderText(Lang.Get("curveostockage:recettes-recherche"));
        if (recherche.Length > 0) champ.SetValue(recherche);
        Filtrer();
    }

    public override void OnGuiOpened()
    {
        if (choisie != null && !IndexRecettes.De(capi).Sorties.Contains(choisie)) choisie = null;
        SingleComposer?.Dispose();
        Composer();
        base.OnGuiOpened();
    }

    /// <summary>Le contenu du réseau a changé : « avec le stock » et les ingrédients manquants sont recalculés.</summary>
    public void Maj() { if (IsOpened()) Filtrer(); }

    private void Filtrer()
    {
        if (SingleComposer?.GetElement("liste") is not ListeRecettes liste) return;
        var index = IndexRecettes.De(capi);
        var s = stock();
        index.MajFaisable(s);
        var mots = recherche.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Garde(IndexRecettes.Sortie x)
        {
            foreach (var m in mots)
            {
                if (m.StartsWith('@')) { if (!x.Pile.Collectible.Code.Domain.StartsWith(m[1..])) return false; }
                else if (!index.NomDe(x).Contains(m)) return false;
            }
            return true;
        }
        var trouvees = index.Sorties.Where(Garde).ToList();
        int faisables = trouvees.Count(x => x.Faisable);
        var affichees = (avecStock ? trouvees.Where(x => x.Faisable) : trouvees)
            .OrderBy(x => x.Faisable ? 0 : 1).ThenBy(x => index.NomDe(x), StringComparer.Ordinal).ToList();
        liste.Definir(affichees, choisie);
        (SingleComposer.GetElement("puces") as PucesStasis)?.Definir(new List<PucesStasis.Puce>
        {
            new("toutes", Lang.Get("curveostockage:recettes-toutes"), trouvees.Count, false),
            new("stock", Lang.Get("curveostockage:recettes-stock"), faisables, false),
        }, avecStock ? "stock" : "toutes");
        MajApercu();
    }

    private void MajApercu() => (SingleComposer?.GetElement("apercu") as ApercuRecette)?.Definir(choisie, stock());

    /// <summary>Grave la recette affichée sur une carte vierge (pour un automate horloger).</summary>
    private void Perforer()
    {
        if (SingleComposer?.GetElement("apercu") is not ApercuRecette apercu || apercu.Recette is not (GridRecipe r, int i)) return;
        envoyer(IdPaquets.Perforer, Vintagestory.API.Util.SerializerUtil.Serialize(new PaquetRemplir { Index = i, Nom = r.Name?.ToString() ?? "", Sortie = PaquetRemplir.CodeSortie(r) }));
    }

    private void Remplir()
    {
        if (SingleComposer?.GetElement("apercu") is not ApercuRecette apercu || apercu.Recette is not (GridRecipe r, int i)) return;
        bool max = capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] || capi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
        envoyer(IdPaquets.RemplirAtelier, Vintagestory.API.Util.SerializerUtil.Serialize(new PaquetRemplir { Index = i, Nom = r.Name?.ToString() ?? "", Sortie = PaquetRemplir.CodeSortie(r), Max = max }));
    }
}
