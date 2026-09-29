using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace CurveoStockage;

/// <summary>Petit bouton carré (−, +) au style du mod.</summary>
public class BoutonPetit : ElementDynamique
{
    private readonly string texte;
    private readonly Action<MouseEvent> clic;

    public BoutonPetit(ICoreClientAPI capi, ElementBounds bounds, string texte, Action<MouseEvent> clic) : base(capi, bounds)
    {
        this.texte = texte; this.clic = clic;
    }

    protected override void Dessiner(Context ctx, double l, double h)
    {
        Style.Bloc(ctx, 0.5, 0.5, l - 1, h - 1, scaled(5), Style.Surface2, Style.TraitFort);
        var f = Style.Police(15, Style.LaitonClair, gras: true);
        Style.Ecrire(api, ctx, f, texte, (l - Style.Largeur(f, texte)) / 2, (h - scaled(15)) / 2 - scaled(2));
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        api.Gui.PlaySound("menubutton_press");
        clic(args);
        args.Handled = true;
    }
}

/// <summary>Automate horloger : 9 cartes (objet fabriqué, stock à garder), cadence selon l'axe, file de fabrication du réseau.</summary>
public class GuiAutomate : FenetreBloc
{
    private const double L = 3 * 150 + 2 * 8;
    private readonly BEAutomate automate;

    public GuiAutomate(ICoreClientAPI capi, BEAutomate automate) : base(Lang.Get("curveostockage:automate-titre"), automate.Inventory, automate.Pos, capi)
    {
        this.automate = automate;
        if (IsDuplicate) return;
        var bornes = new List<ElementBounds>();
        var ajouts = new List<Action<GuiComposer>>();
        double y = 38;

        var vitesseB = ElementBounds.Fixed(0, y + 4, L - 150, 22);
        var badgeB = ElementBounds.Fixed(L - 140, y, 140, 28);
        var etatB = ElementBounds.Fixed(0, y + 30, L, 20);
        bornes.AddRange(new[] { vitesseB, badgeB, etatB });
        ajouts.Add(c =>
        {
            c.AddDynamicText("", Style.Police(14, Style.Texte2, gras: true), vitesseB, "vitesse");
            c.AddInteractiveElement(new BadgeStasis(capi, badgeB), "badge");
            c.AddDynamicText("", Style.Police(13, Style.Moyen), etatB, "etat");
        });
        y += 58;

        var cartesEtB = ElementBounds.Fixed(0, y, L, 16);
        bornes.Add(cartesEtB);
        ajouts.Add(c => c.AddStaticText(Lang.Get("curveostockage:section-cartes").ToUpperInvariant(), Etiquette(), cartesEtB));
        y += 22;
        for (int i = 0; i < BEAutomate.NbCartes; i++)
        {
            int n = i;
            double x0 = (i % 3) * 158, y0 = y + (i / 3) * 100;
            var fondB = ElementBounds.Fixed(x0, y0, 150, 92);
            var caseB = ElementStdBounds.SlotGrid(EnumDialogArea.None, x0 + 8, y0 + 8, 1, 1);
            var nomB = ElementBounds.Fixed(x0 + 62, y0 + 10, 84, 48);
            var moinsB = ElementBounds.Fixed(x0 + 6, y0 + 62, 24, 24);
            var valeurB = ElementBounds.Fixed(x0 + 32, y0 + 66, 86, 18);
            var plusB = ElementBounds.Fixed(x0 + 120, y0 + 62, 24, 24);
            bornes.AddRange(new[] { fondB, caseB, nomB, moinsB, valeurB, plusB });
            ajouts.Add(c =>
            {
                c.AddStaticElement(new SurfaceStasis(capi, fondB, Style.Surface, Style.Trait));
                c.AddInteractiveElement(new GrilleStasis(capi, Inventory, DoSendPacket, 1, new[] { n }, caseB), "case" + n);
                c.AddDynamicText("", Style.Police(12, Style.Texte), nomB, "nom" + n);
                c.AddInteractiveElement(new BoutonPetit(capi, moinsB, "−", a => Garder(n, -1)), "moins" + n);
                c.AddDynamicText("", Style.Police(11.5, Style.Texte3).WithOrientation(EnumTextOrientation.Center), valeurB, "garder" + n);
                c.AddInteractiveElement(new BoutonPetit(capi, plusB, "+", a => Garder(n, 1)), "plus" + n);
            });
        }
        y += 3 * 100 + 8;

        var fileEtB = ElementBounds.Fixed(0, y, L, 16);
        var fileB = ElementBounds.Fixed(0, y + 22, L, 128);
        var fileTexteB = ElementBounds.Fixed(12, y + 30, L - 24, 112);
        bornes.AddRange(new[] { fileEtB, fileB, fileTexteB });
        ajouts.Add(c =>
        {
            c.AddStaticText(Lang.Get("curveostockage:section-file").ToUpperInvariant(), Etiquette(), fileEtB);
            c.AddStaticElement(new SurfaceStasis(capi, fileB, Style.Creux, Style.Trait));
            c.AddDynamicText("", Style.Police(13, Style.Texte2), fileTexteB, "file");
        });
        y += 22 + 128 + 12;
        var viderB = ElementBounds.Fixed(0, y, 190, 44);
        var aideB = ElementBounds.Fixed(202, y, L - 202, 60);
        bornes.AddRange(new[] { viderB, aideB });
        ajouts.Add(c =>
        {
            c.AddInteractiveElement(new BoutonMode(capi, viderB, Lang.Get("curveostockage:automate-vider"), Lang.Get("curveostockage:automate-vider-effet"),
                () => capi.Network.SendBlockEntityPacket(BlockEntityPosition, BEAutomate.PaquetViderFile)), "vider");
            c.AddStaticText(Lang.Get("curveostockage:automate-aide"), Style.Police(12.5, Style.Texte3), aideB);
        });

        var composer = Commencer("curveostockage-automate", Fond(bornes));
        foreach (var a in ajouts) a(composer);
        composer.EndChildElements();
        SingleComposer = composer.Compose();
        Inventory.SlotModified += _ => Maj();
        Maj();
    }

    /// <summary>Clic ±1, Maj ±10, Ctrl ±64.</summary>
    private void Garder(int i, int sens)
    {
        var k = capi.Input.KeyboardKeyState;
        int pas = k[(int)GlKeys.ControlLeft] || k[(int)GlKeys.ControlRight] ? 64 : k[(int)GlKeys.ShiftLeft] || k[(int)GlKeys.ShiftRight] ? 10 : 1;
        var data = new byte[5];
        data[0] = (byte)i;
        BitConverter.GetBytes(sens * pas).CopyTo(data, 1);
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BEAutomate.PaquetGarder, data);
    }

    public void Maj()
    {
        if (SingleComposer == null) return;
        for (int i = 0; i < BEAutomate.NbCartes; i++)
        {
            var carte = Inventory[i].Itemstack;
            bool perforee = ItemCarte.Perforee(carte);
            string nom = perforee ? ItemCarte.Sortie(capi.World, carte!)?.GetName() ?? Lang.Get("curveostockage:carte-inconnue") : Lang.Get("curveostockage:automate-case-vide");
            if (nom.Length > 30) nom = nom[..29] + "…";
            SingleComposer.GetDynamicText("nom" + i)?.SetNewText(nom);
            int garder = perforee ? ItemCarte.Garder(carte!) : 0;
            SingleComposer.GetDynamicText("garder" + i)?.SetNewText(!perforee ? "" : garder > 0 ? Lang.Get("curveostockage:automate-garder", garder) : Lang.Get("curveostockage:automate-sur-commande"));
        }
        float axe = automate.VitesseAxe;
        SingleComposer.GetDynamicText("vitesse")?.SetNewText(axe > 0.001f
            ? Lang.Get("curveostockage:automate-axe", automate.Cadence.ToString("0.#"))
            : Lang.Get("curveostockage:automate-sans-axe"));
        SingleComposer.GetDynamicText("etat")?.SetNewText(automate.Etat.Length > 0 ? Lang.Get(automate.Etat) : "");
        (SingleComposer.GetElement("badge") as BadgeStasis)?.Definir(
            automate.Etat.Length > 0 ? Lang.Get("curveostockage:badge-bus-arret") : automate.Actif ? Lang.Get("curveostockage:badge-automate-travail") : Lang.Get("curveostockage:badge-automate-repos"),
            automate.Etat.Length > 0 ? BadgeStasis.Ton.Alerte : automate.Actif ? BadgeStasis.Ton.Actif : BadgeStasis.Ton.Neutre);
        var lignes = automate.File.Take(7).Select(c =>
        {
            string l = "• " + Lang.Get("curveostockage:file-ligne", c.Restant, c.Total, c.NomSortie)
                       + (c.Stock ? " " + Lang.Get("curveostockage:file-stock") : c.Qui.Length > 0 ? " — " + c.Qui : "");
            return c.Manque.Length > 0 ? l + "  ⚠ " + Lang.Get("curveostockage:file-manque", c.Manque) : l;
        }).ToList();
        if (automate.File.Count > 7) lignes.Add(Lang.Get("curveostockage:file-autres", automate.File.Count - 7));
        SingleComposer.GetDynamicText("file")?.SetNewText(lignes.Count == 0 ? Lang.Get("curveostockage:file-vide") : string.Join("\n", lignes));
    }
}

/// <summary>Commande d'un objet fabricable depuis le terminal ou la tablette : quantité, raccourcis, envoi.</summary>
public class GuiCommande : GuiDialog
{
    private readonly ItemStack pile;
    private readonly string cle;
    private readonly int parFabrication;
    private readonly Action<int, byte[]?> envoyer;
    private readonly DummyInventory apercu;

    public override string? ToggleKeyCombinationCode => null;
    private static GuiCommande? ouverte;

    /// <summary>Une seule fenêtre de commande à la fois, au premier plan.</summary>
    public static void Ouvrir(ICoreClientAPI capi, ItemStack pile, string cle, int parFabrication, Action<int, byte[]?> envoyer)
    {
        ouverte?.TryClose();
        ouverte = new GuiCommande(capi, pile, cle, parFabrication, envoyer);
        ouverte.TryOpen();
        ouverte.Focus();
    }

    public GuiCommande(ICoreClientAPI capi, ItemStack pile, string cle, int parFabrication, Action<int, byte[]?> envoyer) : base(capi)
    {
        this.pile = pile.Clone(); this.cle = cle; this.parFabrication = Math.Max(1, parFabrication); this.envoyer = envoyer;
        apercu = new DummyInventory(capi, 1);
        apercu[0].Itemstack = this.pile;
        const double L = 330;
        var caseB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 38, 1, 1);
        var nomB = ElementBounds.Fixed(60, 44, L - 60, 40);
        var qteB = ElementBounds.Fixed(0, 102, 120, 32);
        var raccourcis = new[] { this.parFabrication, 16, 64, Math.Max(1, this.pile.Collectible.MaxStackSize) }.Distinct().Take(4).ToArray();
        var raccB = raccourcis.Select((_, i) => ElementBounds.Fixed(130 + i * 50, 104, 44, 28)).ToArray();
        var noteB = ElementBounds.Fixed(0, 144, L, 20);
        var envoyerB = ElementBounds.Fixed(0, 172, L, 48);
        var fond = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding + 6);
        fond.BothSizing = ElementSizing.FitToChildren;
        fond.WithChildren(new[] { caseB, nomB, qteB, noteB, envoyerB }.Concat(raccB).ToArray());
        // En haut de l'écran, au-dessus du terminal (centré) plutôt que dessous
        var dialogue = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterTop).WithFixedAlignmentOffset(0, GuiStyle.DialogToScreenPadding);
        Positions.Oublier(capi, "curveostockage-commande");
        var c = capi.Gui.CreateCompo("curveostockage-commande", dialogue);
        c.AddStaticElement(new CadreStasis(capi, fond));
        c.AddInteractiveElement(new TitreStasis(capi, Lang.Get("curveostockage:commande-titre"), c, () => TryClose()), "titre");
        c.BeginChildElements(fond);
        c.AddPassiveItemSlot(caseB, apercu, apercu[0]);
        c.AddStaticText(this.pile.GetName(), Style.Police(15, Style.Texte, gras: true), nomB);
        c.AddTextInput(qteB, _ => { }, Style.Police(15, Style.Texte), "qte");
        for (int i = 0; i < raccourcis.Length; i++)
        {
            int v = raccourcis[i];
            c.AddInteractiveElement(new BoutonPetit(capi, raccB[i], v.ToString(), _ => SingleComposer?.GetTextInput("qte")?.SetValue(v.ToString())), "r" + i);
        }
        c.AddStaticText(Lang.Get("curveostockage:commande-note", this.parFabrication), Style.Police(12.5, Style.Texte3), noteB);
        c.AddInteractiveElement(new BoutonMode(capi, envoyerB, Lang.Get("curveostockage:commande-envoyer"), Lang.Get("curveostockage:commande-envoyer-effet"), Envoyer), "envoyer");
        c.EndChildElements();
        SingleComposer = c.Compose();
        SingleComposer.GetTextInput("qte").SetValue(this.parFabrication.ToString());
    }

    private void Envoyer()
    {
        if (!int.TryParse(SingleComposer?.GetTextInput("qte")?.GetText()?.Trim(), out int q) || q <= 0) return;
        envoyer(IdPaquets.Commander, SerializerUtil.Serialize(new PaquetCommande { Cle = cle, Quantite = Math.Min(q, 100_000) }));
        TryClose();
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        Dispose();
    }
}
