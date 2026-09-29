using Cairo;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>Un évènement du journal d'un réseau. Type : 0 rangé, 1 retiré, 2 alerte de pourriture, 3 mode du stabilisateur.</summary>
[ProtoContract]
public class EvenementJournal
{
    [ProtoMember(1)] public long Temps;
    [ProtoMember(2)] public string Qui = "";
    [ProtoMember(3)] public int Type;
    /// <summary>Objet concerné, « b:domaine:code » ou « i:domaine:code ».</summary>
    [ProtoMember(4)] public string Code = "";
    [ProtoMember(5)] public int Quantite;

    public static string CodeDe(ItemStack pile) => (pile.Class == EnumItemClass.Block ? "b:" : "i:") + pile.Collectible.Code;

    public static ItemStack? Pile(string code, IWorldAccessor world)
    {
        if (code.Length < 3) return null;
        var loc = new AssetLocation(code[2..]);
        CollectibleObject? obj = code[0] == 'b' ? world.GetBlock(loc) : world.GetItem(loc);
        return obj == null ? null : new ItemStack(obj);
    }
}

/// <summary>Liste d'activité : icône de l'objet, phrase, ancienneté ; défile à la molette.</summary>
public class ListeActivite : ElementDynamique
{
    private List<(EvenementJournal e, ItemStack? pile)> lignes = new();
    private int debut;
    private double HauteurLigne => scaled(42);

    public ListeActivite(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(IEnumerable<EvenementJournal> journal)
    {
        lignes = journal.OrderByDescending(e => e.Temps).Select(e => (e, EvenementJournal.Pile(e.Code, api.World))).ToList();
        debut = Math.Clamp(debut, 0, Math.Max(0, lignes.Count - Visibles));
        Rafraichir();
    }

    private int Visibles => Math.Max(1, (int)((Bounds.InnerHeight - scaled(8)) / HauteurLigne));

    private string Phrase(EvenementJournal e, ItemStack? pile)
    {
        var nom = pile?.GetName() ?? e.Code;
        return e.Type switch
        {
            0 => Lang.Get("curveostockage:journal-range", e.Qui, e.Quantite, nom),
            1 => Lang.Get("curveostockage:journal-retire", e.Qui, e.Quantite, nom),
            2 => Lang.Get("curveostockage:journal-alerte", nom, e.Quantite),
            4 => Lang.Get("curveostockage:journal-fabrique", e.Qui, e.Quantite, nom),
            _ => Lang.Get("curveostockage:journal-mode", e.Qui, Lang.Get("curveostockage:mode-" + e.Quantite)),
        };
    }

    private static string Anciennete(long temps)
    {
        var min = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - temps) / 60000;
        return min < 1 ? Lang.Get("curveostockage:temps-maintenant") : min < 60 ? Lang.Get("curveostockage:temps-min", min) : Lang.Get("curveostockage:temps-h", min / 60);
    }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(8), Style.Creux, Style.Trait);
        if (lignes.Count == 0)
        {
            var fv = Style.Police(14, Style.Texte3);
            var t = Lang.Get("curveostockage:journal-vide");
            Style.Ecrire(api, ctx, fv, t, (l - Style.Largeur(fv, t)) / 2, h / 2 - scaled(10));
            return;
        }
        var ft = Style.Police(14.5, Style.Texte2);
        var fh = Style.Police(13, Style.Texte3);
        double y = scaled(4);
        for (int i = debut; i < lignes.Count && i < debut + Visibles; i++)
        {
            var (e, pile) = lignes[i];
            double x = scaled(48);
            if (e.Type is 2 or 3 || pile == null)
            {
                var fond = e.Type == 2 ? Style.Alpha(Style.Moyen, 0.14) : Style.Alpha(Style.Lueur, 0.14);
                Style.Bloc(ctx, scaled(10), y + scaled(7), scaled(28), scaled(28), scaled(6), fond);
                var fs = Style.Police(14, e.Type == 2 ? Style.Moyen : Style.Lueur, gras: true);
                string s = e.Type == 2 ? "!" : "◷";
                Style.Ecrire(api, ctx, fs, s, scaled(24) - Style.Largeur(fs, s) / 2, y + scaled(11));
            }
            string anc = Anciennete(e.Temps);
            double la = Style.Largeur(fh, anc);
            Style.Ecrire(api, ctx, fh, anc, l - la - scaled(10), y + scaled(12));
            string phrase = Phrase(e, pile);
            double max = l - x - la - scaled(24);
            while (phrase.Length > 4 && Style.Largeur(ft, phrase) > max) phrase = phrase[..^2] + "…";
            Style.Ecrire(api, ctx, ft, phrase, x, y + scaled(11));
            if (i < debut + Visibles - 1 && i < lignes.Count - 1)
            {
                Style.Couleur(ctx, new[] { 0.165, 0.125, 0.086, 1.0 }); ctx.LineWidth = 1;
                ctx.MoveTo(scaled(8), y + HauteurLigne - 0.5); ctx.LineTo(l - scaled(8), y + HauteurLigne - 0.5); ctx.Stroke();
            }
            y += HauteurLigne;
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        double y = Bounds.renderY + scaled(4);
        for (int i = debut; i < lignes.Count && i < debut + Visibles; i++)
        {
            var (e, pile) = lignes[i];
            if (e.Type is 0 or 1 or 4 && pile != null)
                api.Render.RenderItemstackToGui(new DummySlot(pile), Bounds.renderX + scaled(24), y + HauteurLigne / 2, 100, (float)scaled(26), -1, showStackSize: false);
            y += HauteurLigne;
        }
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY)) return;
        int nouveau = Math.Clamp(debut - Math.Sign(args.delta), 0, Math.Max(0, lignes.Count - Visibles));
        if (nouveau != debut) { debut = nouveau; Rafraichir(); }
        args.SetHandled(true);
    }
}
