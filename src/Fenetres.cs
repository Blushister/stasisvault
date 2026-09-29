using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>Outils communs aux fenêtres de blocs du mod (placement à côté de l'inventaire, cadre, titre).</summary>
public abstract class FenetreBloc : GuiDialogBlockEntity
{
    protected EnumPosFlag position;
    protected static double Case => GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;

    protected FenetreBloc(string titre, InventoryBase inventaire, BlockPos pos, ICoreClientAPI capi) : base(titre, inventaire, pos, capi) { }

    protected GuiComposer Commencer(string nom, ElementBounds fond)
    {
        position = GetFreePos("smallblockgui");
        var dialogue = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(IsRight(position) ? EnumDialogArea.RightMiddle : EnumDialogArea.LeftMiddle)
            .WithFixedAlignmentOffset(IsRight(position) ? -GuiStyle.DialogToScreenPadding : GuiStyle.DialogToScreenPadding, 0);
        var c = capi.Gui.CreateCompo(nom + BlockEntityPosition, dialogue);
        c.AddStaticElement(new CadreStasis(capi, fond));
        c.AddInteractiveElement(new TitreStasis(capi, DialogTitle, c, () => TryClose()), "titre");
        c.BeginChildElements(fond);
        return c;
    }

    protected static ElementBounds Fond(IEnumerable<ElementBounds> enfants)
    {
        var fond = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding + 6);
        fond.BothSizing = ElementSizing.FitToChildren;
        fond.WithChildren(enfants.ToArray());
        return fond;
    }

    protected static CairoFont Etiquette() => Style.Police(12, Style.Laiton, gras: true);

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        if (capi.Gui.GetDialogPosition(SingleComposer.DialogName) == null) OccupyPos("smallblockgui", position);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        FreePos("smallblockgui", position);
    }
}

/// <summary>Carte d'une alvéole : fond (plein ou pointillé si libre), nom, effet, barre et chiffres.</summary>
public class CarteAlveole : ElementDynamique
{
    private string? nom, note;
    private long objets, objetsMax, types, typesMax;

    private readonly Action<MouseEvent>? clic;

    public CarteAlveole(ICoreClientAPI capi, ElementBounds bounds, Action<MouseEvent>? clic = null) : base(capi, bounds) { this.clic = clic; }

    /// <summary>Un clic n'importe où sur la carte agit sur l'alvéole (prendre ou poser le cylindre, comme un disque).</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (clic == null) return;
        clic(args);
        args.Handled = true;
    }

    public void Vide() { nom = null; Rafraichir(); }
    public void Definir(string nom, string? note, long o, long om, long t, long tm) { this.nom = nom; this.note = note; objets = o; objetsMax = om; types = t; typesMax = tm; Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        double x0 = scaled(64);
        if (nom == null)
        {
            GuiElement.RoundRectangle(ctx, 0.5, 0.5, l - 1, h - 1, scaled(8));
            Style.Couleur(ctx, new[] { 0.227, 0.176, 0.106, 1.0 }); ctx.LineWidth = 1; ctx.SetDash(new[] { scaled(4), scaled(3) }, 0); ctx.Stroke(); ctx.SetDash(Array.Empty<double>(), 0);
            Style.Ecrire(api, ctx, Style.Police(14, Style.Texte3), Lang.Get("curveostockage:alveole-libre"), x0, h / 2 - scaled(10));
            return;
        }
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(8), Style.Surface, Style.Trait);
        var fn = Style.Police(15, Style.Texte, gras: true);
        Style.Ecrire(api, ctx, fn, nom, x0, scaled(8));
        if (note != null)
        {
            var fe = Style.Police(13, Style.Texte3);
            Style.Ecrire(api, ctx, fe, note, l - scaled(10) - Style.Largeur(fe, note), scaled(9));
        }
        Style.Barre(api, ctx, x0, scaled(30), l - x0 - scaled(10), scaled(8), (double)objets / Math.Max(1, objetsMax));
        var fd = Style.Police(13, Style.Texte2);
        Style.Ecrire(api, ctx, fd, $"{objets:N0} / {objetsMax:N0}", x0, scaled(42));
        string t = Lang.Get("curveostockage:types-court", types, typesMax);
        Style.Ecrire(api, ctx, fd, t, l - scaled(10) - Style.Largeur(fd, t), scaled(42));
    }
}

/// <summary>Baie : 8 alvéoles en cartes (2 colonnes × 4), comme la façade du bloc.</summary>
public class GuiBaie : FenetreBloc
{
    public GuiBaie(ICoreClientAPI capi, BEBaie baie) : base(Lang.Get("curveostockage:baie-titre"), baie.Inventory, baie.Pos, capi)
    {
        if (IsDuplicate) return;
        var bornes = new List<ElementBounds>();
        var cartes = new ElementBounds[8]; var cases = new ElementBounds[8];
        for (int i = 0; i < 8; i++)
        {
            double x = (i % 2) * 272, y = 38 + (i / 2) * 72;
            cartes[i] = ElementBounds.Fixed(x, y, 264, 64);
            cases[i] = ElementStdBounds.SlotGrid(EnumDialogArea.None, x + 8, y + 8, 1, 1);
            bornes.Add(cartes[i]); bornes.Add(cases[i]);
        }
        var aideB = ElementBounds.Fixed(0, 38 + 4 * 72 + 4, 536, 20); bornes.Add(aideB);
        var c = Commencer("curveostockage-baie", Fond(bornes));
        for (int i = 0; i < 8; i++)
        {
            int alveole = i;
            c.AddInteractiveElement(new CarteAlveole(capi, cartes[i], args =>
            {
                var touches = capi.Input.KeyboardKeyState;
                (SingleComposer?.GetElement("case" + alveole) as GrilleStasis)?.SlotClick(capi, alveole, args.Button,
                    touches[(int)GlKeys.ShiftLeft] || touches[(int)GlKeys.ShiftRight],
                    touches[(int)GlKeys.ControlLeft] || touches[(int)GlKeys.ControlRight],
                    touches[(int)GlKeys.AltLeft] || touches[(int)GlKeys.AltRight]);
            }), "carte" + i);
            c.AddInteractiveElement(new GrilleStasis(capi, Inventory, DoSendPacket, 1, new[] { i }, cases[i]), "case" + i);
        }
        c.AddStaticText(Lang.Get("curveostockage:baie-aide"), Style.Police(14, Style.Texte3), aideB);
        c.EndChildElements();
        SingleComposer = c.Compose();
        Inventory.SlotModified += _ => Maj();
        Maj();
    }

    private void Maj()
    {
        if (SingleComposer == null) return;
        for (int i = 0; i < 8; i++)
        {
            if (SingleComposer.GetElement("carte" + i) is not CarteAlveole carte) continue;
            var pile = Inventory[i].Itemstack;
            if (pile?.Collectible is not ItemCylindre cyl) { carte.Vide(); continue; }
            var (capObjets, capTypes) = StockageSystem.Config.Capacite(cyl);
            var m = cyl.Variant["materiau"];
            var effet = StockageSystem.Config.Effet(m);
            string? note = effet == "refrigere" ? Lang.Get("curveostockage:bouton-effet-1") : effet == "stase" ? Lang.Get("curveostockage:note-stase") : null;
            carte.Definir(Lang.Get("curveostockage:court-" + m), note, pile.Attributes.GetLong("cylObjets"), capObjets,
                pile.Attributes.GetInt("cylTypes"), capTypes);
        }
    }
}

/// <summary>Autonomie en grand (Lora), réserve exacte à droite.</summary>
public class ChiffreAutonomie : ElementDynamique
{
    private string valeur = "—", unite = "", droite = "";

    public ChiffreAutonomie(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

    public void Definir(string valeur, string unite, string droite) { this.valeur = valeur; this.unite = unite; this.droite = droite; Rafraichir(); }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        var fg = Style.PoliceTitre(26, Style.Texte);
        var fu = Style.Police(14, Style.Texte2, gras: true);
        var fd = Style.Police(14, Style.Texte3);
        Style.Ecrire(api, ctx, fg, valeur, 0, 0);
        Style.Ecrire(api, ctx, fu, unite, Style.Largeur(fg, valeur) + scaled(6), scaled(10));
        Style.Ecrire(api, ctx, fd, droite, l - Style.Largeur(fd, droite), scaled(10));
    }
}

/// <summary>Bloc « Carburant » commun au stabilisateur et à l'ancre.</summary>
public abstract class FenetreCarburant : FenetreBloc
{
    protected const double L = 440;
    protected FenetreCarburant(string titre, InventoryBase inv, BlockPos pos, ICoreClientAPI capi) : base(titre, inv, pos, capi) { }

    /// <summary>Ajoute le bloc Carburant à partir de y ; renvoie le y suivant.</summary>
    protected double Carburant(List<ElementBounds> bornes, List<Action<GuiComposer>> ajouts, double y)
    {
        var etiquetteB = ElementBounds.Fixed(0, y, L, 16);
        var carteB = ElementBounds.Fixed(0, y + 22, L, 96);
        var caseB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 12, y + 22 + 24, 1, 1);
        var chiffreB = ElementBounds.Fixed(76, y + 22 + 10, L - 88, 32);
        var cransB = ElementBounds.Fixed(76, y + 22 + 46, L - 88, 12);
        var noteB = ElementBounds.Fixed(76, y + 22 + 66, L - 88, 20);
        bornes.AddRange(new[] { etiquetteB, carteB, caseB, chiffreB, cransB, noteB });
        ajouts.Add(c =>
        {
            c.AddStaticText(Lang.Get("curveostockage:section-carburant").ToUpperInvariant(), Etiquette(), etiquetteB);
            c.AddStaticElement(new SurfaceStasis(capi, carteB, Style.Surface, Style.Trait));
            c.AddInteractiveElement(new GrilleStasis(capi, Inventory, DoSendPacket, 1, null, caseB), "carburant");
            c.AddInteractiveElement(new ChiffreAutonomie(capi, chiffreB), "autonomie");
            c.AddInteractiveElement(new CransStasis(capi, cransB), "crans");
            c.AddStaticText(Lang.Get("curveostockage:autonomie-note"), Style.Police(13.5, Style.Texte3), noteB);
        });
        return y + 22 + 96 + 16;
    }

    protected void MajCarburant(double carburant, double? jours, double chargeMax, bool consomme = true)
    {
        if (!consomme)
        {
            (SingleComposer?.GetElement("crans") as CransStasis)?.Definir(1, 1);
            (SingleComposer?.GetElement("autonomie") as ChiffreAutonomie)?.Definir("∞", "", Lang.Get("curveostockage:sans-consommation"));
            return;
        }
        // Au-delà de 32 charges, les crans représentent une fraction de la réserve
        int crans = (int)Math.Clamp(Math.Ceiling(chargeMax), 1, 32);
        (SingleComposer?.GetElement("crans") as CransStasis)?.Definir(carburant * crans / Math.Max(1e-6, chargeMax), crans);
        int max = (int)Math.Round(chargeMax);
        (SingleComposer?.GetElement("autonomie") as ChiffreAutonomie)?.Definir(
            jours is double j ? j.ToString("0.#") : "—", jours != null ? Lang.Get("curveostockage:unite-jours") : "",
            Lang.Get("curveostockage:reserve-engrenages", carburant.ToString("0.#"), max));
    }

    protected (string texte, BadgeStasis.Ton ton) Tempete()
    {
        var sd = capi.ModLoader.GetModSystem<SystemTemporalStability>()?.StormData;
        if (sd == null) return ("", BadgeStasis.Ton.Neutre);
        if (sd.nowStormActive) return (Lang.Get("curveostockage:tempete-en-cours"), BadgeStasis.Ton.Alerte);
        double jours = sd.nextStormTotalDays - capi.World.Calendar.TotalDays;
        return jours > 0 && jours < 5 ? (Lang.Get("curveostockage:tempete-dans", jours.ToString("0")), BadgeStasis.Ton.Alerte) : ("", BadgeStasis.Ton.Neutre);
    }
}

public class GuiStabilisateur : FenetreCarburant
{
    private readonly BEStabilisateur stab;

    public GuiStabilisateur(ICoreClientAPI capi, BEStabilisateur stab) : base(Lang.Get("curveostockage:stab-titre"), stab.Inventory, stab.Pos, capi)
    {
        this.stab = stab;
        if (IsDuplicate) return;
        var bornes = new List<ElementBounds>(); var ajouts = new List<Action<GuiComposer>>();
        double y = Carburant(bornes, ajouts, 38);
        var modeB = ElementBounds.Fixed(0, y, L, 16);
        var modes = BEStabilisateur.ModesDisponibles;
        int nb = modes.Count;
        double lb = (L - 6 * (nb - 1)) / nb;
        var boutons = Enumerable.Range(0, nb).Select(i => ElementBounds.Fixed(i * (lb + 6), y + 22, lb, 52)).ToArray();
        var effetB = ElementBounds.Fixed(0, y + 84, L, 22);
        var etatB = ElementBounds.Fixed(0, y + 116, 200, 28);
        var tempeteB = ElementBounds.Fixed(L - 230, y + 116, 230, 28);
        bornes.AddRange(boutons); bornes.AddRange(new[] { modeB, effetB, etatB, tempeteB });
        var c = Commencer("curveostockage-stabilisateur", Fond(bornes));
        foreach (var a in ajouts) a(c);
        c.AddStaticText(Lang.Get("curveostockage:mode").ToUpperInvariant(), Etiquette(), modeB);
        for (int i = 0; i < nb; i++)
        {
            int mode = modes[i];
            c.AddInteractiveElement(new BoutonMode(capi, boutons[i], Lang.Get("curveostockage:bouton-mode-" + i), Lang.Get("curveostockage:bouton-effet-" + i),
                () => capi.Network.SendBlockEntityPacket(BlockEntityPosition, BEStabilisateur.PaquetMode + mode)), "mode" + mode);
        }
        c.AddDynamicText("", Style.Police(15, Style.Texte), effetB, "effet");
        c.AddInteractiveElement(new BadgeStasis(capi, etatB), "etat");
        c.AddInteractiveElement(new BadgeStasis(capi, tempeteB), "tempete");
        c.EndChildElements();
        SingleComposer = c.Compose();
        Maj();
    }

    public void Maj()
    {
        if (SingleComposer == null) return;
        MajCarburant(stab.Carburant, stab.Mode == 0 ? null : stab.Carburant * stab.HeuresParEngrenage(stab.Mode) / 24, stab.ChargeMax, stab.Consomme);
        foreach (int i in BEStabilisateur.ModesDisponibles) if (SingleComposer.GetElement("mode" + i) is BoutonMode b) b.Actif = stab.Mode == i;
        SingleComposer.GetDynamicText("effet")?.SetNewText(Lang.Get("curveostockage:desc-mode-" + stab.Mode));
        bool enMarche = stab.Mode > 0 && (stab.Carburant > 0 || !stab.Consomme);
        (SingleComposer.GetElement("etat") as BadgeStasis)?.Definir(
            stab.Mode == 0 ? Lang.Get("curveostockage:badge-arret") : enMarche ? Lang.Get("curveostockage:badge-marche") : Lang.Get("curveostockage:badge-vide"),
            stab.Mode == 0 ? BadgeStasis.Ton.Neutre : enMarche ? BadgeStasis.Ton.Actif : BadgeStasis.Ton.Alerte);
        var (t, ton) = Tempete();
        (SingleComposer.GetElement("tempete") as BadgeStasis)?.Definir(t, ton);
    }
}

public class GuiAncre : FenetreCarburant
{
    private readonly BEAncre ancre;

    public GuiAncre(ICoreClientAPI capi, BEAncre ancre) : base(Lang.Get("curveostockage:ancre-titre"), ancre.Inventory, ancre.Pos, capi)
    {
        this.ancre = ancre;
        if (IsDuplicate) return;
        var bornes = new List<ElementBounds>(); var ajouts = new List<Action<GuiComposer>>();
        double y = Carburant(bornes, ajouts, 38);
        var zoneB = ElementBounds.Fixed(0, y, L, 16);
        var carteFondB = ElementBounds.Fixed(0, y + 22, L, 176);
        var carteB = ElementBounds.Fixed(12, y + 34, 152, 152);
        var legendeB = ElementBounds.Fixed(182, y + 40, L - 194, 140);
        var etatB = ElementBounds.Fixed(0, y + 212, 130, 28);
        var proprioB = ElementBounds.Fixed(142, y + 217, L - 142, 22);
        bornes.AddRange(new[] { zoneB, carteFondB, carteB, legendeB, etatB, proprioB });
        var c = Commencer("curveostockage-ancre", Fond(bornes));
        foreach (var a in ajouts) a(c);
        c.AddStaticText(Lang.Get("curveostockage:section-zone").ToUpperInvariant(), Etiquette(), zoneB);
        c.AddStaticElement(new SurfaceStasis(capi, carteFondB, Style.Surface, Style.Trait));
        c.AddInteractiveElement(new CarteChunks(capi, carteB), "carte");
        c.AddInteractiveElement(new LegendeStasis(capi, legendeB), "legende");
        c.AddInteractiveElement(new BadgeStasis(capi, etatB), "etat");
        c.AddDynamicText("", Style.Police(14, Style.Texte3), proprioB, "proprio");
        c.EndChildElements();
        SingleComposer = c.Compose();
        Maj();
    }

    public void Maj()
    {
        if (SingleComposer == null) return;
        var cfg = StockageSystem.Config;
        MajCarburant(ancre.Carburant, ancre.Carburant > 0 ? ancre.Carburant * cfg.Ancre.HeuresParCharge / 24 : null, ancre.ChargeMax, ancre.Consomme);
        (SingleComposer.GetElement("carte") as CarteChunks)?.Definir(ancre.CarteGardees, ancre.CarteReseau);
        int hors = ancre.CarteReseau.Except(ancre.CarteGardees).Count(i => i != 12);
        var legende = new List<(double[]? plein, double[]? contour, string texte)>
        {
            (Style.Lueur, null, Lang.Get("curveostockage:legende-gardees", ancre.NbColonnes, cfg.Ancre.ColonnesMax)),
            (null, Style.Moyen, Lang.Get("curveostockage:legende-hors", hors)),
            (Style.Laiton, null, Lang.Get("curveostockage:legende-ancre")),
        };
        // Réseau plus étendu que la limite : on le dit, seules les colonnes les plus proches sont gardées
        if (ancre.Limitee) legende.Add((null, Style.Bas, Lang.Get("curveostockage:legende-limitee")));
        (SingleComposer.GetElement("legende") as LegendeStasis)?.Definir(legende);
        bool active = ancre.NbColonnes > 0;
        (SingleComposer.GetElement("etat") as BadgeStasis)?.Definir(active ? Lang.Get("curveostockage:badge-active") : Lang.Get("curveostockage:badge-inactive"),
            active ? BadgeStasis.Ton.Actif : BadgeStasis.Ton.Neutre);
        SingleComposer.GetDynamicText("proprio")?.SetNewText(string.IsNullOrEmpty(ancre.ProprietaireNom)
            ? Lang.Get("curveostockage:legende-regle") : Lang.Get("curveostockage:proprio-regle", ancre.ProprietaireNom));
    }
}
