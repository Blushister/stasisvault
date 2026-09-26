using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>
/// Infobulle du stockage, dans le style Stasis : icône, nom et mod, quantité en stock, fraîcheur et vitesse de
/// pourrissement, description du jeu (nutrition, durabilité…) et rappel des clics. Remplace celle du jeu sur les grilles virtuelles.
/// </summary>
public class InfobulleStasis : IDisposable
{
    private readonly ICoreClientAPI capi;
    private LoadedTexture texture;
    private string signature = "";
    private ItemStack? pile;
    private double icone;

    public InfobulleStasis(ICoreClientAPI capi)
    {
        this.capi = capi;
        texture = new LoadedTexture(capi);
    }

    private static double S(double v) => GuiElement.scaled(v);

    private static List<string> Envelopper(CairoFont f, string texte, double largeur)
    {
        var lignes = new List<string>();
        foreach (var paragraphe in texte.Split('\n'))
        {
            var ligne = "";
            foreach (var mot in paragraphe.Split(' '))
            {
                var essai = ligne.Length == 0 ? mot : ligne + " " + mot;
                if (ligne.Length > 0 && Style.Largeur(f, essai) > largeur) { lignes.Add(ligne); ligne = mot; }
                else ligne = essai;
            }
            lignes.Add(ligne);
        }
        return lignes;
    }

    private string NomDuMod(string domaine)
        => domaine == "game" ? "Vintage Story" : capi.ModLoader.GetMod(domaine)?.Info?.Name ?? domaine;

    /// <summary>Recompose la texture si le contenu a changé.</summary>
    private void Composer(ItemSlot slot, long quantite, float? fraicheur, float taux)
    {
        var p = slot.Itemstack!;
        // La pile affichée est recréée à chaque liste reçue : son identité suffit à savoir si l'objet a changé
        string sig = $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p)}|{quantite}|{(fraicheur is float f0 ? Math.Round(f0 * 100) : -1)}|{taux:0.###}";
        if (sig == signature && texture.TextureId != 0) return;
        signature = sig;
        pile = p;

        double L = S(330), marge = S(12);
        icone = S(44);
        var fNom = Style.Police(16, Style.LaitonClair, gras: true);
        var fMod = Style.Police(12.5, Style.Texte3);
        var fTexte = Style.Police(13.5, Style.Texte2);
        var fFort = Style.Police(14, Style.Texte, gras: true);
        var fAide = Style.Police(12, Style.Texte3);
        double lTexte = L - 2 * marge;

        string nom = p.GetName();
        var lignesNom = Envelopper(fNom, nom, L - 3 * marge - icone);
        string mod = NomDuMod(p.Collectible.Code.Domain);

        // Description du jeu, sans la ligne « Mod : … » que l'on affiche déjà en tête
        var dsc = new StringBuilder();
        try { p.Collectible.GetHeldItemInfo(slot, dsc, capi.World, false); } catch { }
        // Le jeu écrit en VTML (<font color=…>, <strong>…) : on garde le texte seul
        var brut = System.Text.RegularExpressions.Regex.Replace(dsc.ToString(), "<[^>]*>", "");
        var desc = System.Net.WebUtility.HtmlDecode(brut).Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => !(l.Contains(mod) && l.Contains(':')))
            .ToList();
        while (desc.Count > 0 && desc[^1].Length == 0) desc.RemoveAt(desc.Count - 1);
        var lignesDesc = Envelopper(fTexte, string.Join("\n", desc.Take(10)), lTexte);
        if (lignesDesc.Count > 12) { lignesDesc = lignesDesc.Take(12).ToList(); lignesDesc[^1] += " …"; }

        double hLigne = S(19);
        double hEntete = Math.Max(icone, lignesNom.Count * S(20) + S(18));
        var lignesAide = Envelopper(fAide, Lang.Get("curveostockage:infobulle-aide"), lTexte);
        double h = marge + hEntete + S(12) + S(24) + (fraicheur != null ? S(26) : 0)
                   + (lignesDesc.Count > 0 && lignesDesc.Any(l => l.Length > 0) ? S(8) + lignesDesc.Count * hLigne : 0)
                   + S(12) + lignesAide.Count * S(16) + marge;

        using var surf = new ImageSurface(Format.Argb32, (int)L, (int)h);
        using var ctx = new Context(surf);
        // Fond et cadre de laiton fin
        Style.Bloc(ctx, 0.5, 0.5, L - 1, h - 1, S(8), Style.Alpha(Style.Panneau, 0.97), Style.TraitFort, 1.5);
        Style.Bloc(ctx, S(3), S(3), L - S(6), h - S(6), S(6), Style.Alpha(Style.Panneau, 0), Style.Alpha(Style.Laiton, 0.25));

        // En-tête : case de l'icône, nom, mod
        Style.Bloc(ctx, marge, marge, icone, icone, S(6), Style.Creux, Style.Trait);
        double x = marge * 2 + icone, y = marge;
        foreach (var l in lignesNom) { Style.Ecrire(capi, ctx, fNom, l, x, y); y += S(20); }
        Style.Ecrire(capi, ctx, fMod, mod, x, y + S(1));
        y = marge + hEntete + S(8);
        Style.Couleur(ctx, Style.Trait); ctx.LineWidth = 1; ctx.MoveTo(marge, y + 0.5); ctx.LineTo(L - marge, y + 0.5); ctx.Stroke();
        y += S(8);

        // Stock
        string stock = Lang.Get("curveostockage:infobulle-stock", quantite.ToString("N0"));
        Style.Ecrire(capi, ctx, fFort, stock, marge, y);
        y += S(24);

        // Fraîcheur : barre + vitesse actuelle
        if (fraicheur is float f)
        {
            var couleur = f > 0.6f ? Style.Ok : f > 0.3f ? Style.Moyen : Style.Bas;
            string etat = taux <= 0.001f ? Lang.Get("curveostockage:infobulle-fige")
                : taux < 0.999f ? Lang.Get("curveostockage:infobulle-ralenti", taux.ToString("0.##"))
                : Lang.Get("curveostockage:infobulle-normal");
            var fEtat = Style.Police(12.5, taux < 0.999f ? Style.Lueur : Style.Texte3, gras: true);
            double lEtat = Style.Largeur(fEtat, etat);
            string pourcent = Lang.Get("curveostockage:infobulle-fraicheur", (int)Math.Round(f * 100));
            var fP = Style.Police(13, couleur, gras: true);
            double lP = Style.Largeur(fP, pourcent);
            Style.Ecrire(capi, ctx, fP, pourcent, marge, y);
            Style.Ecrire(capi, ctx, fEtat, etat, L - marge - lEtat, y + S(1));
            double xb = marge + lP + S(10), lb = L - marge - lEtat - S(10) - xb, yb = y + S(6);
            if (lb > S(20))
            {
                Style.Bloc(ctx, xb, yb, lb, S(7), S(3.5), Style.Creux, Style.Trait);
                if (f > 0.01f) Style.Bloc(ctx, xb, yb, Math.Max(S(7), lb * f), S(7), S(3.5), couleur);
            }
            y += S(26);
        }

        // Description du jeu
        if (lignesDesc.Any(l => l.Length > 0))
        {
            y += S(8);
            foreach (var l in lignesDesc) { Style.Ecrire(capi, ctx, fTexte, l, marge, y); y += hLigne; }
        }

        // Rappel des clics
        y += S(12);
        Style.Couleur(ctx, Style.Trait); ctx.LineWidth = 1; ctx.MoveTo(marge, y - S(6) + 0.5); ctx.LineTo(L - marge, y - S(6) + 0.5); ctx.Stroke();
        foreach (var l in lignesAide) { Style.Ecrire(capi, ctx, fAide, l, marge, y); y += S(16); }

        capi.Gui.LoadOrUpdateCairoTexture(surf, true, ref texture);
    }

    /// <summary>Affiche l'infobulle près de la souris, sans sortir de l'écran.</summary>
    public void Afficher(ItemSlot slot, long quantite, float? fraicheur, float taux)
    {
        if (slot.Itemstack == null) return;
        Composer(slot, quantite, fraicheur, taux);
        if (texture.TextureId == 0 || pile == null) return;
        double mx = capi.Input.MouseX, my = capi.Input.MouseY;
        double x = mx + S(18), y = my + S(14);
        if (x + texture.Width > capi.Render.FrameWidth - S(4)) x = mx - texture.Width - S(12);
        if (y + texture.Height > capi.Render.FrameHeight - S(4)) y = capi.Render.FrameHeight - texture.Height - S(4);
        x = Math.Max(S(4), x); y = Math.Max(S(4), y);
        capi.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, (float)x, (float)y, texture.Width, texture.Height, 1000);
        double m = S(12);
        capi.Render.RenderItemstackToGui(new DummySlot(pile), x + m + icone / 2, y + m + icone / 2, 1010, (float)(icone * 0.62), -1, showStackSize: false);
    }

    public void Dispose() => texture.Dispose();
}
