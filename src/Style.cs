using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>
/// Système visuel des fenêtres Stasis Vault (maquette v3) : cadre de laiton fin, surfaces unies sombres pour
/// que le texte reste lisible, textures réservées au cadre et au verre temporel. Titres en Lora (livrée avec le jeu).
/// </summary>
public static class Style
{
    public static readonly double[] Panneau = C(0x19, 0x13, 0x0d), Surface = C(0x22, 0x1a, 0x12), Surface2 = C(0x2c, 0x22, 0x17), Creux = C(0x12, 0x0d, 0x08);
    public static readonly double[] Trait = C(0x43, 0x34, 0x1f), TraitFort = C(0x6a, 0x52, 0x30);
    public static readonly double[] Texte = C(0xf6, 0xee, 0xdc), Texte2 = C(0xd8, 0xcb, 0xae), Texte3 = C(0xab, 0x9c, 0x7f);
    public static readonly double[] Laiton = C(0xd4, 0xac, 0x5e), LaitonClair = C(0xec, 0xd6, 0xa0), LaitonSombre = C(0x5b, 0x3f, 0x14);
    public static readonly double[] Lueur = C(0x6f, 0xdc, 0xcd), Ok = C(0x8f, 0xd1, 0x6a), Moyen = C(0xe0, 0xb6, 0x4a), Bas = C(0xde, 0x6a, 0x42);

    private static double[] C(int r, int g, int b, double a = 1) => new[] { r / 255.0, g / 255.0, b / 255.0, a };
    public static double[] Alpha(double[] c, double a) => new[] { c[0], c[1], c[2], a };

    public static CairoFont Police(double taille, double[] couleur, bool gras = false)
    {
        var f = new CairoFont(taille + 1, GuiStyle.StandardFontName, couleur);
        if (gras) f.FontWeight = FontWeight.Bold;
        return f;
    }
    public static CairoFont PoliceTitre(double taille, double[] couleur)
    {
        var f = new CairoFont(taille, GuiStyle.DecorativeFontName, couleur);
        f.FontWeight = FontWeight.Bold;
        return f;
    }

    public static void Couleur(Context ctx, double[] c) => ctx.SetSourceRGBA(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1);

    /// <summary>Rectangle arrondi rempli, avec bordure facultative.</summary>
    public static void Bloc(Context ctx, double x, double y, double l, double h, double r, double[] fond, double[]? bord = null, double epaisseur = 1)
    {
        GuiElement.RoundRectangle(ctx, x, y, l, h, r);
        Couleur(ctx, fond);
        if (bord == null) { ctx.Fill(); return; }
        ctx.FillPreserve();
        Couleur(ctx, bord);
        ctx.LineWidth = epaisseur;
        ctx.Stroke();
    }

    /// <summary>Remplit un rectangle avec une texture du mod, agrandie de <paramref name="grossi"/> (pixels nets).</summary>
    public static void Texture(ICoreClientAPI capi, Context ctx, string texture, double x, double y, double l, double h, double grossi = 2)
    {
        var motif = GuiElement.getPattern(capi, new AssetLocation("curveostockage:textures/" + texture + ".png"), doCache: true);
        motif.Filter = Filter.Nearest;
        motif.Extend = Extend.Repeat;
        var m = new Matrix();
        double f = GuiElement.scaled(grossi);
        m.Scale(1 / f, 1 / f);
        m.Translate(-x, -y);
        motif.Matrix = m;
        ctx.Save();
        ctx.Rectangle(x, y, l, h);
        ctx.SetSource(motif);
        ctx.Fill();
        ctx.Restore();
    }

    public static void Rivet(Context ctx, double cx, double cy, double r)
    {
        using var g = new RadialGradient(cx - r * 0.3, cy - r * 0.3, r * 0.1, cx, cy, r);
        g.AddColorStop(0, new Color(1, 0.955, 0.815, 1));
        g.AddColorStop(0.55, new Color(0.72, 0.565, 0.247, 1));
        g.AddColorStop(1, new Color(0.337, 0.224, 0.059, 1));
        ctx.Arc(cx, cy, r, 0, Math.PI * 2);
        ctx.SetSource(g);
        ctx.Fill();
    }

    public static double Largeur(CairoFont f, string texte) => f.GetTextExtents(texte).Width;

    /// <summary>Écrit une ligne ; DrawTextLine garde la couleur courante du contexte, on applique donc celle de la police.</summary>
    public static void Ecrire(ICoreClientAPI capi, Context ctx, CairoFont f, string texte, double x, double y)
    {
        f.SetupContext(ctx);
        Couleur(ctx, f.Color);
        capi.Gui.Text.DrawTextLine(ctx, f, texte, x, y);
    }

    /// <summary>Barre de capacité : verre temporel ; ambre à 85 % ; rouge quand c'est plein.</summary>
    public static void Barre(ICoreClientAPI capi, Context ctx, double x, double y, double l, double h, double fraction)
    {
        Bloc(ctx, x, y, l, h, h / 2, Creux, Trait);
        double f = Math.Clamp(fraction, 0, 1), lf = f * l;
        if (lf < 1) return;
        ctx.Save();
        GuiElement.RoundRectangle(ctx, x, y, lf, h, h / 2);
        ctx.Clip();
        if (fraction >= 1) Degrade(ctx, x, y, lf, h, C(0xb7, 0x51, 0x2f), Bas);
        else if (fraction >= 0.85) Degrade(ctx, x, y, lf, h, C(0xc9, 0x9a, 0x3a), Moyen);
        else Texture(capi, ctx, "block/verre-temporel", x, y, lf, h, 1);
        ctx.Restore();
    }

    private static void Degrade(Context ctx, double x, double y, double l, double h, double[] a, double[] b)
    {
        using var g = new LinearGradient(x, 0, x + l, 0);
        g.AddColorStop(0, new Color(a[0], a[1], a[2], 1));
        g.AddColorStop(1, new Color(b[0], b[1], b[2], 1));
        ctx.Rectangle(x, y, l, h);
        ctx.SetSource(g);
        ctx.Fill();
    }

    public static string Abreger(long n) => n >= 1_000_000 ? (n / 100_000 / 10.0).ToString("0.#") + "M"
        : n >= 10_000 ? (n / 1000) + "k" : n.ToString("N0");
}

/// <summary>Fond de fenêtre : cadre de laiton fin et riveté, intérieur uni.</summary>
public class CadreStasis : GuiElement
{
    public CadreStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        double x = Bounds.bgDrawX, y = Bounds.bgDrawY, l = Bounds.OuterWidth, h = Bounds.OuterHeight, e = scaled(5);
        Style.Texture(api, ctx, "gui/laiton", x, y, l, h, 2);
        using (var g = new LinearGradient(0, y, 0, y + h))
        {
            g.AddColorStop(0, new Color(1, 0.93, 0.75, 0.25));
            g.AddColorStop(1, new Color(0.2, 0.12, 0.02, 0.35));
            ctx.Rectangle(x, y, l, h); ctx.SetSource(g); ctx.Fill();
        }
        Style.Couleur(ctx, Style.LaitonSombre); ctx.LineWidth = 1.5; ctx.Rectangle(x + 0.75, y + 0.75, l - 1.5, h - 1.5); ctx.Stroke();
        Style.Bloc(ctx, x + e, y + e, l - 2 * e, h - 2 * e, scaled(5), Style.Panneau, new[] { 0.04, 0.03, 0.02, 1.0 });
        double r = scaled(2.6), m = scaled(2.6);
        Style.Rivet(ctx, x + m, y + m, r); Style.Rivet(ctx, x + l - m, y + m, r);
        Style.Rivet(ctx, x + m, y + h - m, r); Style.Rivet(ctx, x + l - m, y + h - m, r);
    }
}

/// <summary>Barre de titre sombre, titre en Lora laiton ; déplacement et fermeture du jeu conservés.</summary>
public class TitreStasis : GuiElementDialogTitleBar
{
    private readonly string titre;

    public TitreStasis(ICoreClientAPI capi, string titre, GuiComposer composer, Action fermer)
        : base(capi, titre, composer, fermer, Style.PoliceTitre(18, Style.LaitonClair))
    {
        this.titre = titre;
    }

    public override void ComposeTextElements(Context ctx, ImageSurface surface)
    {
        base.ComposeTextElements(ctx, surface);
        double x = Bounds.bgDrawX, y = Bounds.bgDrawY, l = Bounds.OuterWidth, h = Bounds.OuterHeight;
        Style.Texture(api, ctx, "block/noyer", x, y, l, h, 2);
        Style.Couleur(ctx, Style.Alpha(Style.Panneau, 0.9)); ctx.Rectangle(x, y, l, h); ctx.Fill();
        Style.Couleur(ctx, Style.TraitFort); ctx.LineWidth = 1; ctx.MoveTo(x, y + h - 0.5); ctx.LineTo(x + l, y + h - 0.5); ctx.Stroke();
        var f = Style.PoliceTitre(18, Style.LaitonClair);
        Style.Ecrire(api, ctx, f, titre, x + scaled(14), y + (h - scaled(18)) / 2 - scaled(3));
        // Bouton de fermeture, à l'emplacement de celui du jeu
        double t = scaled(GuiElementDialogTitleBar.unscaledCloseIconSize), b = t + scaled(10);
        double bx = x + l - b - scaled(6), by = y + (h - b) / 2;
        Style.Bloc(ctx, bx, by, b, b, scaled(4), Style.Surface, Style.TraitFort);
        Style.Couleur(ctx, Style.Texte2); ctx.LineWidth = scaled(2);
        double p = scaled(8);
        ctx.MoveTo(bx + p, by + p); ctx.LineTo(bx + b - p, by + b - p);
        ctx.MoveTo(bx + b - p, by + p); ctx.LineTo(bx + p, by + b - p);
        ctx.Stroke();
    }
}

/// <summary>Surface statique (fond creux d'une grille, carte, séparateur…).</summary>
public class SurfaceStasis : GuiElement
{
    private readonly double[] fond;
    private readonly double[]? bord;
    private readonly double rayon;
    private readonly bool pointille;

    public SurfaceStasis(ICoreClientAPI capi, ElementBounds bounds, double[] fond, double[]? bord, double rayon = 8, bool pointille = false) : base(capi, bounds)
    {
        this.fond = fond; this.bord = bord; this.rayon = rayon; this.pointille = pointille;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        if (pointille && bord != null)
        {
            GuiElement.RoundRectangle(ctx, Bounds.drawX + 0.5, Bounds.drawY + 0.5, Bounds.InnerWidth - 1, Bounds.InnerHeight - 1, scaled(rayon));
            Style.Couleur(ctx, bord); ctx.LineWidth = 1; ctx.SetDash(new[] { scaled(4), scaled(3) }, 0); ctx.Stroke(); ctx.SetDash(Array.Empty<double>(), 0);
            return;
        }
        Style.Bloc(ctx, Bounds.drawX + 0.5, Bounds.drawY + 0.5, Bounds.InnerWidth - 1, Bounds.InnerHeight - 1, scaled(rayon), fond, bord);
    }
}

/// <summary>Flèche vers le bas (grille d'artisanat → résultat).</summary>
public class FlecheStasis : GuiElement
{
    public FlecheStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        double cx = Bounds.drawX + Bounds.InnerWidth / 2, y0 = Bounds.drawY + scaled(2), h = Bounds.InnerHeight - scaled(4), d = scaled(7);
        Style.Couleur(ctx, Style.Laiton); ctx.LineWidth = scaled(2.5);
        ctx.MoveTo(cx, y0); ctx.LineTo(cx, y0 + h); ctx.Stroke();
        ctx.MoveTo(cx - d, y0 + h - d); ctx.LineTo(cx, y0 + h); ctx.LineTo(cx + d, y0 + h - d); ctx.Stroke();
    }
}

/// <summary>Grille d'emplacements réels (baie, dépôt, carburant) aux cases plates du mod.</summary>
public class GrilleStasis : GuiElementItemSlotGrid
{
    protected readonly bool cible;

    public GrilleStasis(ICoreClientAPI capi, IInventory inventaire, Action<object> envoyer, int colonnes, int[]? emplacements, ElementBounds bounds, bool cible = false)
        : base(capi, inventaire, envoyer, colonnes, emplacements, bounds) { this.cible = cible; }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        base.ComposeElements(ctx, surface);
        int s = (int)scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        using (var surf = new ImageSurface(Format.Argb32, s, s))
        using (var c = new Context(surf))
        {
            if (cible)
            {
                Style.Bloc(c, 0.5, 0.5, s - 1, s - 1, scaled(4), Style.Creux, Style.Lueur);
                Style.Couleur(c, Style.Alpha(Style.Lueur, 0.55)); c.LineWidth = scaled(1.6);
                double m = s / 2.0, d = scaled(7);
                c.MoveTo(m - d, m); c.LineTo(m + d, m); c.MoveTo(m, m - d); c.LineTo(m, m + d); c.Stroke();
            }
            else
            {
                Style.Bloc(c, 0.5, 0.5, s - 1, s - 1, scaled(4), Style.Surface, new[] { 0.227, 0.176, 0.106, 1.0 });
                using var g = new LinearGradient(0, 0, 0, scaled(5));
                g.AddColorStop(0, new Color(0, 0, 0, 0.45)); g.AddColorStop(1, new Color(0, 0, 0, 0));
                GuiElement.RoundRectangle(c, 1, 1, s - 2, scaled(5), scaled(3)); c.SetSource(g); c.Fill();
            }
            generateTexture(surf, ref slotTexture);
        }
        int sh = s + 4;
        using (var surf = new ImageSurface(Format.Argb32, sh, sh))
        using (var c = new Context(surf))
        {
            Style.Bloc(c, 1, 1, sh - 2, sh - 2, scaled(5), Style.Alpha(Style.Lueur, 0.12), Style.Alpha(Style.Lueur, 0.35), 2);
            Style.Bloc(c, 2.5, 2.5, sh - 5, sh - 5, scaled(4), Style.Alpha(Style.Lueur, 0), Style.Lueur, 1);
            generateTexture(surf, ref highlightSlotTexture);
        }
    }
}

/// <summary>
/// Grille du contenu du réseau : chaque case montre un seul exemplaire ; la quantité (abrégée en « k »),
/// la barre de fraîcheur verticale à gauche et l'épingle sont dessinées par-dessus.
/// </summary>
public class GrilleReseau : GrilleStasis
{
    private readonly System.Func<int, (long quantite, float? fraicheur, bool epingle, bool fabricable)> infos;
    private readonly Dictionary<string, LoadedTexture> textes = new();
    private readonly CairoFont police = new CairoFont(13, GuiStyle.StandardFontName, new double[] { 1, 1, 1, 1 }).WithStroke(new double[] { 0, 0, 0, 1 }, 2);
    private LoadedTexture? epingle, rouage;

    public GrilleReseau(ICoreClientAPI capi, IInventory vue, int colonnes, ElementBounds bounds, System.Func<int, (long, float?, bool, bool)> infos)
        : base(capi, vue, _ => { }, colonnes, null, bounds)
    {
        this.infos = infos;
        police.FontWeight = FontWeight.Bold;
    }

    // Survol géré ici : pas d'infobulle du jeu, la nôtre (InfobulleStasis) ; la case survolée reste connue du jeu (touche H du guide).
    private int survol = -1;
    private InfobulleStasis? bulle;

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        int trouve = -1;
        if (Bounds.ParentBounds.PointInside(args.X, args.Y))
            for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
                if (SlotBounds[i].PointInside(args.X, args.Y)) { trouve = renderedSlots.GetKeyAtIndex(i); break; }
        hoverSlotId = trouve;
        var gestion = api.World.Player.InventoryManager;
        if (trouve >= 0 && inventory[trouve] is ItemSlot sous && !sous.Empty) CaseSurvolee(gestion, sous);
        else if (survol >= 0 && gestion.CurrentHoveredSlot?.Inventory == inventory) CaseSurvolee(gestion, null);
        survol = trouve;
    }

    private static System.Reflection.FieldInfo? champSurvol;
    private static bool champCherche;

    /// <summary>
    /// Déclare la case survolée au jeu (touche H du guide) sans passer par le setter, qui ouvrirait l'infobulle du jeu.
    /// </summary>
    private static void CaseSurvolee(IPlayerInventoryManager gestion, ItemSlot? slot)
    {
        if (!champCherche)
        {
            champCherche = true;
            for (var t = gestion.GetType(); t != null && champSurvol == null; t = t.BaseType)
                champSurvol = t.GetField("currentHoveredSlot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        }
        champSurvol?.SetValue(gestion, slot);
    }

    /// <summary>Clic sur la grille virtuelle : la demande part au serveur (InventaireVue), sans rappeler l'infobulle du jeu.</summary>
    public override void SlotClick(ICoreClientAPI api, int slotId, EnumMouseButton mouseButton, bool shiftPressed, bool ctrlPressed, bool altPressed)
    {
        var touches = (EnumModifierKey)((shiftPressed ? 2 : 0) | (ctrlPressed ? 1 : 0) | (altPressed ? 4 : 0));
        var op = new ItemStackMoveOperation(api.World, mouseButton, touches, EnumMergePriority.AutoMerge) { ActingPlayer = api.World.Player };
        var souris = api.World.Player.InventoryManager.GetOwnInventory("mouse")[0];
        inventory.ActivateSlot(slotId, souris, ref op);
    }

    private LoadedTexture Epingle()
    {
        if (epingle != null) return epingle;
        int t = (int)scaled(12);
        using var surf = new ImageSurface(Format.Argb32, t, t);
        using var c = new Context(surf);
        c.Arc(t / 2.0, t / 2.0 - 1, t / 3.2, 0, Math.PI * 2);
        Style.Couleur(c, Style.LaitonClair); c.FillPreserve();
        Style.Couleur(c, new[] { 0.1, 0.07, 0.02, 1.0 }); c.LineWidth = 1; c.Stroke();
        c.MoveTo(t / 2.0, t / 2.0 + t / 4.0); c.LineTo(t / 2.0, t - 0.5);
        Style.Couleur(c, Style.LaitonClair); c.LineWidth = scaled(1.6); c.Stroke();
        epingle = new LoadedTexture(api);
        generateTexture(surf, ref epingle);
        return epingle;
    }

    /// <summary>Petit rouage : l'objet peut être fabriqué par un automate du réseau.</summary>
    private LoadedTexture Rouage()
    {
        if (rouage != null) return rouage;
        int t = (int)scaled(14);
        using var surf = new ImageSurface(Format.Argb32, t, t);
        using var c = new Context(surf);
        double m = t / 2.0, r1 = t / 2.0 - 0.5, r2 = t / 2.0 - scaled(3);
        for (int k = 0; k < 16; k++)
        {
            double a = k * Math.PI / 8, r = k % 2 == 0 ? r1 : r2;
            if (k == 0) c.MoveTo(m + r * Math.Cos(a), m + r * Math.Sin(a)); else c.LineTo(m + r * Math.Cos(a), m + r * Math.Sin(a));
        }
        c.ClosePath();
        Style.Couleur(c, Style.Lueur); c.FillPreserve();
        Style.Couleur(c, new[] { 0.02, 0.1, 0.1, 1.0 }); c.LineWidth = 1; c.Stroke();
        c.Arc(m, m, scaled(2), 0, Math.PI * 2);
        Style.Couleur(c, new[] { 0.02, 0.1, 0.1, 1.0 }); c.Fill();
        rouage = new LoadedTexture(api);
        generateTexture(surf, ref rouage);
        return rouage;
    }

    // Surcouches dessinées pendant le rendu de l'interface, au-dessus des objets ; jamais dans PostRender (hors shader gui).
    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        for (int i = 0; i < SlotBounds.Length && i < renderedSlots.Count; i++)
        {
            var (quantite, fraicheur, epingle, fabricable) = infos(renderedSlots.GetKeyAtIndex(i));
            var b = SlotBounds[i];
            if (fabricable)
            {
                // Absent du stock : case voilée, seul le rouage dit qu'on peut le commander
                if (quantite <= 0) api.Render.RenderRectangle((float)b.renderX, (float)b.renderY, 299, (float)b.InnerWidth, (float)b.InnerHeight, unchecked((int)0x99000000));
                var tr = Rouage();
                api.Render.Render2DTexturePremultipliedAlpha(tr.TextureId, (float)(b.renderX + scaled(2)), (float)(b.renderY + b.InnerHeight - tr.Height - scaled(2)), tr.Width, tr.Height, 302);
            }
            if (quantite <= 0) continue;
            if (fraicheur is float f)
            {
                double hb = b.InnerHeight - scaled(10), xb = b.renderX + scaled(3), yb = b.renderY + scaled(5);
                api.Render.RenderRectangle((float)xb, (float)yb, 300, (float)scaled(3), (float)hb, unchecked((int)0x8C000000));
                int couleur = f > 0.6f ? unchecked((int)0xFF6AD18F) : f > 0.3f ? unchecked((int)0xFF4AB6E0) : unchecked((int)0xFF426ADE);
                double hf = hb * Math.Clamp(f, 0, 1);
                api.Render.RenderRectangle((float)xb, (float)(yb + hb - hf), 301, (float)scaled(3), (float)hf, couleur);
            }
            if (epingle)
            {
                var t = Epingle();
                api.Render.Render2DTexturePremultipliedAlpha(t.TextureId, (float)(b.renderX + b.InnerWidth - t.Width - scaled(2)), (float)(b.renderY + scaled(2)), t.Width, t.Height, 302);
            }
            if (quantite > 1)
            {
                var texte = Style.Abreger(quantite);
                if (!textes.TryGetValue(texte, out var tex)) textes[texte] = tex = api.Gui.TextTexture.GenTextTexture(texte, police);
                api.Render.Render2DLoadedTexture(tex, (float)(b.renderX + b.InnerWidth - tex.Width - scaled(3)), (float)(b.renderY + b.InnerHeight - tex.Height + scaled(1)), 303);
            }
        }
        if (survol >= 0 && survol < inventory.Count && inventory[survol] is ItemSlot slot && !slot.Empty && IsPositionInside(api.Input.MouseX, api.Input.MouseY))
        {
            var (quantite, fraicheur, _, _) = infos(survol);
            float taux = (inventory as InventoryBase)?.GetTransitionSpeedMul(EnumTransitionType.Perish, slot.Itemstack) ?? 1f;
            (bulle ??= new InfobulleStasis(api)).Afficher(slot, quantite, fraicheur, taux);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        foreach (var t in textes.Values) t.Dispose();
        textes.Clear();
        epingle?.Dispose();
        rouage?.Dispose();
        bulle?.Dispose();
    }
}

/// <summary>Élément redessiné à la demande : on compose une texture puis on l'affiche.</summary>
public abstract class ElementDynamique : GuiElement
{
    private LoadedTexture texture;
    private bool aRecomposer = true;

    protected ElementDynamique(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { texture = new LoadedTexture(capi); }

    protected abstract void Dessiner(Context ctx, double l, double h);

    public void Rafraichir() => aRecomposer = true;

    public override void ComposeElements(Context ctx, ImageSurface surface) { Bounds.CalcWorldBounds(); aRecomposer = true; }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (aRecomposer)
        {
            aRecomposer = false;
            int l = Math.Max(1, (int)Bounds.InnerWidth), h = Math.Max(1, (int)Bounds.InnerHeight);
            using var surf = new ImageSurface(Format.Argb32, l, h);
            using var ctx = new Context(surf);
            Dessiner(ctx, l, h);
            generateTexture(surf, ref texture);
        }
        api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, Bounds.renderX, Bounds.renderY, Bounds.InnerWidth, Bounds.InnerHeight);
    }

    /// <summary>Position de la souris relative à l'élément (pixels mis à l'échelle).</summary>
    protected (double x, double y) Relatif(MouseEvent e) => (e.X - Bounds.renderX, e.Y - Bounds.renderY);

    public override void Dispose() { base.Dispose(); texture.Dispose(); }
}

/// <summary>Mesure : nom à gauche, « valeur / max » à droite, barre en dessous.</summary>
public class MesureStasis : ElementDynamique
{
    private string nom = "";
    private long valeur, max = 1;

    public MesureStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(string nom, long valeur, long max) { this.nom = nom; this.valeur = valeur; this.max = Math.Max(1, max); Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        var fn = Style.Police(14, Style.Texte2, gras: true);
        Style.Ecrire(api, ctx, fn, nom, 0, 0);
        var fv = Style.Police(14, Style.Texte, gras: true);
        var fm = Style.Police(14, Style.Texte3);
        string v = valeur.ToString("N0"), m = " / " + max.ToString("N0");
        double lm = Style.Largeur(fm, m), lv = Style.Largeur(fv, v);
        Style.Ecrire(api, ctx, fv, v, l - lm - lv, 0);
        Style.Ecrire(api, ctx, fm, m, l - lm, 0);
        double bh = scaled(8);
        Style.Barre(api, ctx, 0.5, h - bh - 0.5, l - 1, bh, (double)valeur / max);
    }
}

/// <summary>Sélecteur segmenté (tri, onglets…) : une option active parmi plusieurs.</summary>
public class SegmentsStasis : ElementDynamique
{
    private readonly string[] options;
    private readonly Action<int> choix;
    private int actif;
    private double[] bords = Array.Empty<double>();

    public SegmentsStasis(ICoreClientAPI capi, ElementBounds bounds, string[] options, int actif, Action<int> choix) : base(capi, bounds)
    {
        this.options = options; this.actif = actif; this.choix = choix;
    }

    public int Actif { get => actif; set { if (actif != value) { actif = value; Rafraichir(); } } }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(6), Style.Creux, Style.Trait);
        double p = scaled(3), ls = (l - 2 * p) / options.Length;
        bords = new double[options.Length + 1];
        for (int i = 0; i < options.Length; i++)
        {
            double x = p + i * ls;
            bords[i] = x;
            if (i == actif) Style.Bloc(ctx, x, p, ls, h - 2 * p, scaled(4), Style.Surface2, Style.TraitFort);
            var f = Style.Police(14, i == actif ? Style.LaitonClair : Style.Texte2, gras: true);
            double lt = Style.Largeur(f, options[i]);
            Style.Ecrire(api, ctx, f, options[i], x + (ls - lt) / 2, (h - scaled(14)) / 2 - scaled(2));
        }
        bords[options.Length] = p + options.Length * ls;
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        var (x, _) = Relatif(args);
        for (int i = 0; i < options.Length; i++)
            if (x >= bords[i] && x < bords[i + 1]) { Actif = i; api.Gui.PlaySound("menubutton_press"); choix(i); break; }
        args.Handled = true;
    }
}

/// <summary>Pastilles de filtre avec compteur ; la pastille « alerte » est en ambre tant qu'elle n'est pas active.</summary>
public class PucesStasis : ElementDynamique
{
    public record Puce(string Id, string Nom, int Nombre, bool Alerte);
    private List<Puce> puces = new();
    private string actif;
    private readonly Action<string> choix;
    private readonly List<(double x, double y, double l, double h, string id)> zones = new();

    public PucesStasis(ICoreClientAPI capi, ElementBounds bounds, string actif, Action<string> choix) : base(capi, bounds) { this.actif = actif; this.choix = choix; }

    public void Definir(List<Puce> puces, string actif) { this.puces = puces; this.actif = actif; Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        zones.Clear();
        double x = 0, y = 0, ph = scaled(28), ecart = scaled(6);
        foreach (var p in puces)
        {
            bool on = p.Id == actif;
            var fn = Style.Police(13.5, on ? Style.Lueur : p.Alerte ? Style.Moyen : Style.Texte2, gras: true);
            var fc = Style.Police(12, on ? Style.Lueur : Style.Texte3, gras: true);
            string nb = p.Nombre.ToString();
            double lp = Style.Largeur(fn, p.Nom) + scaled(6) + Style.Largeur(fc, nb) + scaled(24);
            if (x + lp > l && x > 0) { x = 0; y += ph + ecart; }
            if (y + ph > h) break;
            Style.Bloc(ctx, x + 0.5, y + 0.5, lp - 1, ph - 1, ph / 2, on ? Style.Alpha(Style.Lueur, 0.14) : Style.Surface, on ? Style.Alpha(Style.Lueur, 0.55) : Style.Trait);
            double ty = y + (ph - scaled(14)) / 2 - scaled(2);
            Style.Ecrire(api, ctx, fn, p.Nom, x + scaled(12), ty);
            Style.Ecrire(api, ctx, fc, nb, x + scaled(12) + Style.Largeur(fn, p.Nom) + scaled(6), ty + scaled(1));
            zones.Add((x, y, lp, ph, p.Id));
            x += lp + ecart;
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        var (x, y) = Relatif(args);
        foreach (var z in zones)
            if (x >= z.x && x < z.x + z.l && y >= z.y && y < z.y + z.h) { actif = z.id; Rafraichir(); api.Gui.PlaySound("menubutton_press"); choix(z.id); break; }
        args.Handled = true;
    }
}

/// <summary>Onglets internes soulignés (Contenu / Activité), avec un texte d'information à droite.</summary>
public class OngletsStasis : ElementDynamique
{
    private readonly string[] noms;
    private readonly Action<int> choix;
    private int actif;
    private string info = "";
    private readonly List<(double x, double l)> zones = new();

    public OngletsStasis(ICoreClientAPI capi, ElementBounds bounds, string[] noms, int actif, Action<int> choix) : base(capi, bounds)
    {
        this.noms = noms; this.actif = actif; this.choix = choix;
    }

    public void Info(string texte) { if (info != texte) { info = texte; Rafraichir(); } }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        zones.Clear();
        double x = 0;
        for (int i = 0; i < noms.Length; i++)
        {
            var f = Style.Police(12.5, i == actif ? Style.Laiton : Style.Texte3, gras: true);
            string t = noms[i].ToUpperInvariant();
            double lt = Style.Largeur(f, t);
            Style.Ecrire(api, ctx, f, t, x, scaled(2));
            if (i == actif) { Style.Couleur(ctx, Style.Laiton); ctx.Rectangle(x, h - scaled(2), lt, scaled(2)); ctx.Fill(); }
            zones.Add((x, lt));
            x += lt + scaled(18);
        }
        var fi = Style.Police(13.5, Style.Texte3);
        Style.Ecrire(api, ctx, fi, info, l - Style.Largeur(fi, info), scaled(1));
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        var (x, _) = Relatif(args);
        for (int i = 0; i < zones.Count; i++)
            if (x >= zones[i].x && x < zones[i].x + zones[i].l + scaled(9)) { if (i != actif) { actif = i; Rafraichir(); api.Gui.PlaySound("menubutton_press"); choix(i); } break; }
        args.Handled = true;
    }
}

/// <summary>Badge d'état : pastille ronde et texte ; neutre, actif (turquoise) ou alerte (ambre).</summary>
public class BadgeStasis : ElementDynamique
{
    public enum Ton { Neutre, Actif, Alerte }
    private string texte = "";
    private Ton ton;
    private int barres = -1;

    public BadgeStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    /// <summary>barres : 0..4 pour afficher un indicateur de signal, -1 sinon.</summary>
    public void Definir(string texte, Ton ton, int barres = -1) { this.texte = texte; this.ton = ton; this.barres = barres; Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        if (texte.Length == 0) return;
        var couleur = ton switch { Ton.Actif => Style.Lueur, Ton.Alerte => Style.Moyen, _ => Style.Texte2 };
        var f = Style.Police(13.5, couleur, gras: true);
        double lt = Style.Largeur(f, texte), lb = lt + scaled(barres >= 0 ? 44 : 34);
        double x = l - lb;
        var fond = ton switch { Ton.Actif => Style.Alpha(Style.Lueur, 0.14), Ton.Alerte => Style.Alpha(Style.Moyen, 0.1), _ => Style.Surface };
        var bord = ton switch { Ton.Actif => Style.Alpha(Style.Lueur, 0.45), Ton.Alerte => Style.Alpha(Style.Moyen, 0.45), _ => Style.TraitFort };
        Style.Bloc(ctx, x + 0.5, 0.5, lb - 1, h - 1, (h - 1) / 2, fond, bord);
        double cx = x + scaled(14);
        if (barres >= 0)
        {
            for (int i = 0; i < 4; i++)
            {
                double bh = scaled(4 + i * 2.7);
                Style.Couleur(ctx, i < barres ? couleur : new[] { 0.23, 0.18, 0.11, 1.0 });
                ctx.Rectangle(cx - scaled(4) + i * scaled(4), h / 2 + scaled(6) - bh, scaled(3), bh); ctx.Fill();
            }
            cx += scaled(12);
        }
        else
        {
            ctx.Arc(cx, h / 2, scaled(4), 0, Math.PI * 2);
            Style.Couleur(ctx, ton == Ton.Neutre ? Style.Texte3 : couleur); ctx.Fill();
        }
        Style.Ecrire(api, ctx, f, texte, cx + scaled(10), (h - scaled(14)) / 2 - scaled(2));
    }
}

/// <summary>Aide en « touches » : [Clic] une pile · [Clic droit] la moitié…</summary>
public class AideTouches : ElementDynamique
{
    private readonly (string touche, string texte)[] aides;

    public AideTouches(ICoreClientAPI capi, ElementBounds bounds, params (string, string)[] aides) : base(capi, bounds) { this.aides = aides; }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        var fk = Style.Police(12, Style.Texte, gras: true);
        var ft = Style.Police(13.5, Style.Texte3);
        double x = 0, y = 0, lh = scaled(24);
        foreach (var (touche, texte) in aides)
        {
            double lk = Style.Largeur(fk, touche) + scaled(12), lt = Style.Largeur(ft, texte);
            if (x + lk + scaled(6) + lt > l && x > 0) { x = 0; y += lh; }
            Style.Bloc(ctx, x + 0.5, y + 0.5, lk, scaled(20), scaled(4), Style.Surface2, Style.TraitFort);
            Style.Ecrire(api, ctx, fk, touche, x + scaled(6), y + scaled(3));
            Style.Ecrire(api, ctx, ft, texte, x + lk + scaled(6), y + scaled(2));
            x += lk + scaled(6) + lt + scaled(16);
        }
    }
}

/// <summary>Réserve en crans : un cran par engrenage, le dernier partiellement rempli.</summary>
public class CransStasis : ElementDynamique
{
    private double valeur;
    private int nombre = 16;

    public CransStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(double valeur, int nombre) { this.valeur = valeur; this.nombre = Math.Max(1, nombre); Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        double ecart = scaled(3), lc = (l - ecart * (nombre - 1)) / nombre;
        for (int i = 0; i < nombre; i++)
        {
            double x = i * (lc + ecart);
            Style.Bloc(ctx, x + 0.5, 0.5, lc - 1, h - 1, scaled(2), Style.Creux, Style.Trait);
            double part = Math.Clamp(valeur - i, 0, 1);
            if (part <= 0) continue;
            ctx.Save();
            GuiElement.RoundRectangle(ctx, x + 0.5, 0.5, (lc - 1) * part, h - 1, scaled(2));
            ctx.Clip();
            Style.Texture(api, ctx, "block/verre-temporel", x, 0, lc, h, 1);
            ctx.Restore();
        }
    }
}

/// <summary>Bouton de mode : nom et effet, allumé en turquoise quand il est actif.</summary>
public class BoutonMode : ElementDynamique
{
    private readonly string nom, effet;
    private readonly Action clic;
    private bool actif;

    public BoutonMode(ICoreClientAPI capi, ElementBounds bounds, string nom, string effet, Action clic) : base(capi, bounds)
    {
        this.nom = nom; this.effet = effet; this.clic = clic;
    }

    public bool Actif { get => actif; set { if (actif != value) { actif = value; Rafraichir(); } } }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(6), actif ? Style.Alpha(Style.Lueur, 0.14) : Style.Creux, actif ? Style.Alpha(Style.Lueur, 0.55) : Style.Trait);
        var fn = Style.Police(15, actif ? Style.Lueur : Style.Texte2, gras: true);
        var fe = Style.Police(12.5, actif ? Style.Lueur : Style.Texte3, gras: true);
        Style.Ecrire(api, ctx, fn, nom, (l - Style.Largeur(fn, nom)) / 2, h / 2 - scaled(18));
        Style.Ecrire(api, ctx, fe, effet, (l - Style.Largeur(fe, effet)) / 2, h / 2 + scaled(1));
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        api.Gui.PlaySound("menubutton_press");
        clic();
        args.Handled = true;
    }
}

/// <summary>Carte 5×5 des colonnes de chunks autour de l'ancre (index 0..24, l'ancre au centre = 12).</summary>
public class CarteChunks : ElementDynamique
{
    private HashSet<int> gardees = new(), reseau = new();

    public CarteChunks(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(IEnumerable<int> gardees, IEnumerable<int> reseau) { this.gardees = gardees.ToHashSet(); this.reseau = reseau.ToHashSet(); Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(6), Style.Creux, Style.Trait);
        double p = scaled(6), ecart = scaled(3), c = (Math.Min(l, h) - 2 * p - ecart * 4) / 5;
        for (int i = 0; i < 25; i++)
        {
            double x = p + (i % 5) * (c + ecart), y = p + (i / 5) * (c + ecart);
            if (i == 12) Style.Bloc(ctx, x, y, c, c, scaled(3), Style.Laiton, new[] { 0.43, 0.31, 0.1, 1.0 }, 2);
            else if (gardees.Contains(i))
            {
                ctx.Save(); GuiElement.RoundRectangle(ctx, x, y, c, c, scaled(3)); ctx.Clip();
                Style.Texture(api, ctx, "block/verre-temporel", x, y, c, c, 1.5); ctx.Restore();
            }
            else if (reseau.Contains(i)) Style.Bloc(ctx, x + 1, y + 1, c - 2, c - 2, scaled(3), new[] { 0.106, 0.082, 0.055, 1.0 }, Style.Moyen, 2);
            else Style.Bloc(ctx, x, y, c, c, scaled(3), new[] { 0.106, 0.082, 0.055, 1.0 });
        }
    }
}

/// <summary>Légende : petits carrés de couleur et libellés, sur plusieurs lignes.</summary>
public class LegendeStasis : ElementDynamique
{
    private List<(double[]? plein, double[]? contour, string texte)> lignes = new();

    public LegendeStasis(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(List<(double[]? plein, double[]? contour, string texte)> lignes) { this.lignes = lignes; Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        var f = Style.Police(14, Style.Texte2);
        double y = 0;
        foreach (var (plein, contour, texte) in lignes)
        {
            if (plein != null || contour != null)
            {
                Style.Bloc(ctx, 1, y + scaled(3), scaled(14), scaled(14), scaled(3), plein ?? Style.Creux, contour, 2);
                Style.Ecrire(api, ctx, f, texte, scaled(24), y);
            }
            else Style.Ecrire(api, ctx, f, texte, 0, y);
            y += scaled(26);
        }
    }
}
