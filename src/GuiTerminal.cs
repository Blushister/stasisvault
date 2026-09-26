using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace CurveoStockage;

/// <summary>Inventaire d'affichage (client seulement) : chaque clic devient une demande au serveur, rien n'est déplacé ici.</summary>
public class InventaireVue : InventoryGeneric
{
    private readonly PanneauStockage panneau;
    private readonly bool epingles;

    public InventaireVue(ICoreClientAPI capi, PanneauStockage panneau, int taille, bool epingles)
        : base(taille, "curveostockage-vue", epingles ? "epingles" : "contenu", capi)
    {
        this.panneau = panneau;
        this.epingles = epingles;
    }

    public override object? ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        panneau.Clic(epingles, slotId, op);
        return null;
    }

    public override float GetTransitionSpeedMul(EnumTransitionType transType, ItemStack stack)
        => transType == EnumTransitionType.Perish ? panneau.TauxDe(stack) : 1f;
}

/// <summary>
/// Contenu d'un terminal (recherche, filtres, épinglés, grille, activité, dépôt, capacité), partagé par le terminal
/// posé et la tablette. Les demandes partent par <see cref="envoyer"/> (identifiant, données).
/// </summary>
public class PanneauStockage
{
    public const int Colonnes = 9, Lignes = 5;
    private static double Case => GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
    private static double LargeurGrille => Colonnes * Case;
    private static double Largeur => LargeurGrille + 12 + 22;

    private readonly ICoreClientAPI capi;
    private readonly Action<int, byte[]?> envoyer;
    private readonly InventaireVue vue, vueEpingles;
    private readonly string?[] cles = new string?[Colonnes * Lignes], clesEpingles = new string?[Colonnes];
    private readonly Dictionary<ItemStack, float> taux = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, float?> fraicheurs = new();
    private List<(EntreeListe entree, ItemStack pile)> tout = new(), filtre = new();
    private Dictionary<string, EntreeListe> parCle = new();
    private HashSet<string> epingles = new();
    private List<EvenementJournal> journal = new();
    private PaquetListe? dernier;
    private string recherche = "", categorie = "tout";
    // Atelier : la grille d'artisanat du joueur, affichée quand le réseau a un bloc atelier
    private bool atelier, atelierOuvert;
    private IInventory? GrilleArtisanat => capi.World.Player?.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
    private static double LargeurAtelier => 3 * Case + 12;
    // Recettes (façon JEI) : fenêtre à part, au bord droit de l'écran
    private GuiRecettes? recettes;
    private bool parQuantite = true, activite;
    private int premiereLigne;
    private double distance = -1, portee;

    // Pour recomposer la fenêtre (changement d'onglet)
    private string nom = "", titre = "";
    private ElementBounds? dialogue;
    private Action? fermer;
    private IInventory? depot;
    private Action<object>? paquetDepot;
    private bool avecSignal;
    private Action<GuiComposer>? remplacer;
    public GuiComposer? Composer;

    public PanneauStockage(ICoreClientAPI capi, Action<int, byte[]?> envoyer)
    {
        this.capi = capi;
        this.envoyer = envoyer;
        vue = new InventaireVue(capi, this, Colonnes * Lignes, false);
        vueEpingles = new InventaireVue(capi, this, Colonnes, true);
    }

    public GuiComposer Composer_(string nom, string titre, ElementBounds dialogue, Action fermer, IInventory depot, Action<object> paquetDepot,
        Action<GuiComposer> remplacer, bool signal = false)
    {
        (this.nom, this.titre, this.dialogue, this.fermer, this.depot, this.paquetDepot, this.remplacer, avecSignal) = (nom, titre, dialogue, fermer, depot, paquetDepot, remplacer, signal);
        return Composer = Composer_();
    }

    private GuiComposer Composer_()
    {
        double L = Largeur, y = 38;
        var bornes = new List<ElementBounds>();
        ElementBounds B(double x, double yy, double l, double h) { var b = ElementBounds.Fixed(x, yy, l, h); bornes.Add(b); return b; }

        var rechercheB = B(0, y, L - 206, 34);
        var triB = B(L - 196, y, 196, 34);
        y += 44;
        var pucesB = B(0, y, L, 62);
        y += 70;
        var ongletsB = B(0, y, L, 22);
        y += 32;
        double yContenu = y;
        double hZone = 18 + Case + 12 + 10 + Lignes * Case + 12;
        ElementBounds? etiqAtelierB = null, fondAtelierB = null, grilleAtelierB = null, flecheB = null, sortieB = null, viderB = null;
        if (atelier)
        {
            double xa = L + 18, la = LargeurAtelier, ya = y;
            etiqAtelierB = B(xa, ya, la, 18);
            fondAtelierB = B(xa, ya + 20, la, 3 * Case + 12);
            grilleAtelierB = ElementStdBounds.SlotGrid(EnumDialogArea.None, xa + 6, ya + 26, 3, 3); bornes.Add(grilleAtelierB);
            double yf = ya + 20 + 3 * Case + 12 + 6;
            flecheB = B(xa, yf, la, 22);
            double ys = yf + 28;
            sortieB = ElementStdBounds.SlotGrid(EnumDialogArea.None, xa + (la - GuiElementPassiveItemSlot.unscaledSlotSize) / 2, ys, 1, 1); bornes.Add(sortieB);
            viderB = B(xa, ys + Case + 14, la, 48);
        }
        ElementBounds? etiquetteB = null, fondPinsB = null, pinsB = null, fondGrilleB = null, grilleB = null, defilB = null, listeB = null;
        if (!activite)
        {
            etiquetteB = B(0, y, 200, 18);
            fondPinsB = B(0, y + 20, LargeurGrille + 12, Case + 12);
            pinsB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 6, y + 26, Colonnes, 1); bornes.Add(pinsB);
            double yg = y + 20 + Case + 12 + 10;
            fondGrilleB = B(0, yg, LargeurGrille + 12, Lignes * Case + 12);
            grilleB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 6, yg + 6, Colonnes, Lignes); bornes.Add(grilleB);
            defilB = B(LargeurGrille + 18, yg, 16, Lignes * Case + 12);
        }
        else listeB = B(0, y, L, hZone);
        y += hZone + 12;
        var sepB = B(0, y, L, 1);
        y += 13;
        var depotB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y, 1, 1); bornes.Add(depotB);
        var depotTexteB = B(0, y + Case + 2, 70, 16);
        var objetsB = B(66, y, L - 66 - 180, 34);
        var typesB = B(66, y + 40, L - 66 - 180, 34);
        var signalB = avecSignal ? B(L - 168, y + 2, 168, 28) : null;
        var badgeB = B(L - 168, avecSignal ? y + 40 : y + 22, 168, 28);
        y += 84;
        var aideB = B(0, y, L, 48);

        var fond = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding + 6);
        fond.BothSizing = ElementSizing.FitToChildren;
        fond.WithChildren(bornes.ToArray());

        var c = capi.Gui.CreateCompo(nom, dialogue);
        c.AddStaticElement(new CadreStasis(capi, fond));
        c.AddInteractiveElement(new TitreStasis(capi, titre, c, fermer!), "titre");
        c.BeginChildElements(fond);
        if (signalB != null) c.AddInteractiveElement(new BadgeStasis(capi, signalB), "signal");
        c.AddTextInput(rechercheB, OnRecherche, Style.Police(15, Style.Texte), "recherche");
        c.AddInteractiveElement(new SegmentsStasis(capi, triB, new[] { Lang.Get("curveostockage:tri-nom"), Lang.Get("curveostockage:tri-quantite-court") },
            parQuantite ? 1 : 0, i => { parQuantite = i == 1; Filtrer(); }), "tri");
        c.AddInteractiveElement(new PucesStasis(capi, pucesB, categorie, id => { categorie = id; premiereLigne = 0; Filtrer(); }), "puces");
        c.AddInteractiveElement(new OngletsStasis(capi, ongletsB, new[] { Lang.Get("curveostockage:onglet-contenu"), Lang.Get("curveostockage:onglet-activite") },
            activite ? 1 : 0, i => { activite = i == 1; Recomposer(); }), "onglets");
        if (!activite)
        {
            c.AddStaticText(Lang.Get("curveostockage:epingles").ToUpperInvariant(), Style.Police(12.5, Style.LaitonClair, gras: true), etiquetteB!);
            c.AddStaticElement(new SurfaceStasis(capi, fondPinsB!, Style.Creux, Style.TraitFort));
            c.AddInteractiveElement(new GrilleReseau(capi, vueEpingles, Colonnes, pinsB!, i => Infos(true, i)), "pins");
            // Rangée vide : on explique à quoi elle sert
            c.AddDynamicText("", CairoFont.WhiteSmallText().WithColor(Style.Texte3).WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(fondPinsB!.fixedX, fondPinsB.fixedY + (Case + 12) / 2 - 9, fondPinsB.fixedWidth, 20), "aide-epingles");
            c.AddStaticElement(new SurfaceStasis(capi, fondGrilleB!, Style.Creux, Style.Trait));
            c.AddInteractiveElement(new GrilleReseau(capi, vue, Colonnes, grilleB!, i => Infos(false, i)), "grille");
            c.AddVerticalScrollbar(OnDefilement, defilB!, "defil");
        }
        else c.AddInteractiveElement(new ListeActivite(capi, listeB!), "activite");
        if (atelier && GrilleArtisanat is IInventory grille)
        {
            c.AddStaticText(Lang.Get("curveostockage:section-atelier").ToUpperInvariant(), Style.Police(12.5, Style.LaitonClair, gras: true), etiqAtelierB!);
            c.AddStaticElement(new SurfaceStasis(capi, fondAtelierB!, Style.Creux, Style.TraitFort));
            c.AddInteractiveElement(new GrilleStasis(capi, grille, p => capi.Network.SendPacketClient(p), 3, Enumerable.Range(0, 9).ToArray(), grilleAtelierB!), "atelier");
            c.AddStaticElement(new FlecheStasis(capi, flecheB!));
            c.AddInteractiveElement(new GrilleStasis(capi, grille, p => capi.Network.SendPacketClient(p), 1, new[] { 9 }, sortieB!), "atelier-sortie");
            c.AddInteractiveElement(new BoutonMode(capi, viderB!, Lang.Get("curveostockage:atelier-vider"), Lang.Get("curveostockage:atelier-vider-effet"),
                () => envoyer(IdPaquets.ViderAtelier, new byte[] { 0 })), "atelier-vider");
        }
        c.AddStaticElement(new SurfaceStasis(capi, sepB, Style.Trait, null, 0));
        c.AddInteractiveElement(new GrilleStasis(capi, depot!, paquetDepot!, 1, null, depotB, cible: true), "depot");
        c.AddStaticText(Lang.Get("curveostockage:depot-court").ToUpperInvariant(), Style.Police(11.5, Style.Laiton, gras: true), depotTexteB);
        c.AddInteractiveElement(new MesureStasis(capi, objetsB), "objets");
        c.AddInteractiveElement(new MesureStasis(capi, typesB), "types");
        c.AddInteractiveElement(new BadgeStasis(capi, badgeB), "etat");
        c.AddInteractiveElement(new AideTouches(capi, aideB,
            (Lang.Get("curveostockage:touche-clic"), Lang.Get("curveostockage:aide-pile")),
            (Lang.Get("curveostockage:touche-clic-droit"), Lang.Get("curveostockage:aide-moitie")),
            (Lang.Get("curveostockage:touche-maj-clic"), Lang.Get("curveostockage:aide-inventaire")),
            (Lang.Get("curveostockage:touche-molette"), Lang.Get("curveostockage:aide-epingler"))), "aide");
        c.EndChildElements();
        var composer = c.Compose();
        var champ = composer.GetTextInput("recherche");
        champ.SetPlaceHolderText(Lang.Get("curveostockage:recherche-avancee"));
        if (recherche.Length > 0) champ.SetValue(recherche);

        Composer = composer;
        (composer.GetElement("activite") as ListeActivite)?.Definir(journal);
        Filtrer();
        MajEntete();
        FiltrerRecettes();
        return composer;
    }

    private List<ItemStack> Stock() => tout.Select(x => x.pile).ToList();

    private void FiltrerRecettes() => recettes?.Maj();

    private void OuvrirAtelier()
    {
        if (atelierOuvert || GrilleArtisanat is not IInventory grille) return;
        capi.Network.SendPacketClient(grille.Open(capi.World.Player));
        atelierOuvert = true;
        recettes = GuiRecettes.Pour(capi, Stock, envoyer);
        recettes.TryOpen();
    }

    /// <summary>Rend la grille (au réseau, sinon à l'inventaire) et la referme.</summary>
    private void FermerAtelier()
    {
        recettes?.TryClose();
        if (!atelierOuvert) return;
        atelierOuvert = false;
        envoyer(IdPaquets.ViderAtelier, new byte[] { 1 });
        if (GrilleArtisanat is IInventory grille) capi.Network.SendPacketClient(grille.Close(capi.World.Player));
        (Composer?.GetElement("atelier") as GuiElementItemSlotGridBase)?.OnGuiClosed(capi);
        (Composer?.GetElement("atelier-sortie") as GuiElementItemSlotGridBase)?.OnGuiClosed(capi);
    }

    /// <summary>À appeler quand la fenêtre (terminal ou tablette) se ferme.</summary>
    public void Fermer() => FermerAtelier();

    private void Recomposer()
    {
        var ancien = Composer;
        var nouveau = Composer_();
        remplacer?.Invoke(nouveau);
        ancien?.Dispose();
    }

    public float TauxDe(ItemStack pile) => taux.TryGetValue(pile, out var t) ? t : 1f;

    private (long, float?, bool) Infos(bool pins, int slot)
    {
        var cle = pins ? (slot < clesEpingles.Length ? clesEpingles[slot] : null) : (slot < cles.Length ? cles[slot] : null);
        if (cle == null) return (0, null, false);
        return parCle.TryGetValue(cle, out var e) ? (e.Quantite, fraicheurs.GetValueOrDefault(cle), epingles.Contains(cle)) : (0, null, false);
    }

    public void Signal(double distance, double portee) { this.distance = distance; this.portee = portee; MajEntete(); }

    public void Recevoir(PaquetListe paquet)
    {
        dernier = paquet;
        taux.Clear();
        tout = new();
        foreach (var e in paquet.Entrees)
        {
            var pile = new ItemStack(e.Pile);
            if (!pile.ResolveBlockOrItem(capi.World)) continue;
            pile.StackSize = 1;
            taux[pile] = e.Taux;
            tout.Add((e, pile));
        }
        parCle = tout.GroupBy(x => x.entree.Cle).ToDictionary(g => g.Key, g => g.First().entree);
        fraicheurs.Clear();
        foreach (var (e, pile) in tout) fraicheurs[e.Cle] = Fraicheur(pile);
        epingles = paquet.Epingles.ToHashSet();
        if (paquet.Atelier != atelier)
        {
            atelier = paquet.Atelier;
            if (atelier) OuvrirAtelier(); else FermerAtelier();
            Recomposer();
        }
        journal = paquet.Journal;
        (Composer?.GetElement("activite") as ListeActivite)?.Definir(journal);
        Filtrer();
        MajEntete();
        FiltrerRecettes();
    }

    /// <summary>Part de fraîcheur restante (1 = frais, 0 = pourrit), ou null si l'objet ne pourrit pas.</summary>
    private float? Fraicheur(ItemStack pile)
    {
        var etats = pile.Collectible.UpdateAndGetTransitionStates(capi.World, new DummySlot(pile.Clone(), vue));
        var p = etats?.FirstOrDefault(e => e.Props.Type == EnumTransitionType.Perish);
        if (p == null) return null;
        return p.TransitionLevel > 0 ? 0f : p.FreshHours <= 0 ? 1f : p.FreshHoursLeft / p.FreshHours;
    }

    // --- Filtres rapides et recherche avancée (@mod, #type) ---
    private static readonly string[] Categories = { "tout", "nourriture", "bientot", "metaux", "outils", "blocs" };

    private bool DansCategorie(string cat, ItemStack pile, string cle) => cat switch
    {
        "nourriture" => pile.Collectible.GetNutritionProperties(capi.World, pile, null) != null,
        "bientot" => fraicheurs.GetValueOrDefault(cle) is float f && f < 0.5f,
        "metaux" => pile.Collectible.Code.Path is var p && (p.StartsWith("ingot") || p.StartsWith("metalplate") || p.StartsWith("nugget") || p.StartsWith("metalbit") || p.Contains("metal")),
        "outils" => pile.Collectible.Tool != null,
        "blocs" => pile.Class == EnumItemClass.Block,
        _ => true,
    };

    private bool Correspond(ItemStack pile)
    {
        foreach (var mot in recherche.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (mot.StartsWith('@')) { if (!pile.Collectible.Code.Domain.StartsWith(mot[1..])) return false; }
            else if (mot.StartsWith('#')) { if (!pile.Collectible.FirstCodePart().StartsWith(mot[1..])) return false; }
            else if (!pile.GetName().Contains(mot, StringComparison.CurrentCultureIgnoreCase)) return false;
        }
        return true;
    }

    private void OnRecherche(string texte) { recherche = texte ?? ""; premiereLigne = 0; Filtrer(); }

    private void Filtrer()
    {
        IEnumerable<(EntreeListe entree, ItemStack pile)> r = tout.Where(x => Correspond(x.pile) && DansCategorie(categorie, x.pile, x.entree.Cle));
        r = categorie == "bientot" ? r.OrderBy(x => fraicheurs.GetValueOrDefault(x.entree.Cle) ?? 1f)
            : parQuantite ? r.OrderByDescending(x => x.entree.Quantite).ThenBy(x => x.pile.GetName())
            : r.OrderBy(x => x.pile.GetName(), StringComparer.CurrentCultureIgnoreCase);
        var liste = r.ToList();
        filtre = liste.Where(x => !epingles.Contains(x.entree.Cle)).ToList();
        (Composer?.GetElement("puces") as PucesStasis)?.Definir(Categories.Select(id => new PucesStasis.Puce(id, Lang.Get("curveostockage:filtre-" + id),
            tout.Count(x => DansCategorie(id, x.pile, x.entree.Cle)), id == "bientot")).ToList(), categorie);
        (Composer?.GetElement("onglets") as OngletsStasis)?.Info(activite ? Lang.Get("curveostockage:activite-info")
            : Lang.Get("curveostockage:types-affiches", liste.Count));
        MajDefilement();
        Remplir();
    }

    private void MajEntete()
    {
        if (Composer == null) return;
        if (Composer.GetElement("signal") is BadgeStasis s && distance >= 0)
        {
            double reste = 1 - distance / Math.Max(1, portee);
            int barres = reste > 0.75 ? 4 : reste > 0.5 ? 3 : reste > 0.25 ? 2 : 1;
            s.Definir(Lang.Get("curveostockage:signal", (int)distance, (int)portee), reste < 0.15 ? BadgeStasis.Ton.Alerte : BadgeStasis.Ton.Actif, barres);
        }
        if (dernier == null) return;
        (Composer.GetElement("objets") as MesureStasis)?.Definir(Lang.Get("curveostockage:jauge-objets"), dernier.Objets, dernier.ObjetsMax);
        (Composer.GetElement("types") as MesureStasis)?.Definir(Lang.Get("curveostockage:jauge-types"), dernier.Types, dernier.TypesMax);
        if (Composer.GetElement("etat") is BadgeStasis etat)
        {
            if (dernier.Erreur != null) etat.Definir(Lang.Get(dernier.Erreur), BadgeStasis.Ton.Alerte);
            else if (dernier.StabilisateurActif && dernier.Mode > 0) etat.Definir(Lang.Get("curveostockage:badge-mode-" + dernier.Mode), BadgeStasis.Ton.Actif);
            else etat.Definir(Lang.Get("curveostockage:badge-normal"), BadgeStasis.Ton.Neutre);
        }
    }

    private int LignesTotales => Math.Max(1, (filtre.Count + Colonnes - 1) / Colonnes);

    private void MajDefilement()
    {
        premiereLigne = Math.Clamp(premiereLigne, 0, Math.Max(0, LignesTotales - Lignes));
        Composer?.GetScrollbar("defil")?.SetHeights((float)(Lignes * Case), (float)(Math.Max(LignesTotales, Lignes) * Case));
    }

    private void OnDefilement(float valeur)
    {
        premiereLigne = Math.Clamp((int)Math.Round(valeur / Case), 0, Math.Max(0, LignesTotales - Lignes));
        Remplir();
    }

    private void Defiler(int delta)
    {
        premiereLigne = Math.Clamp(premiereLigne + delta, 0, Math.Max(0, LignesTotales - Lignes));
        var barre = Composer?.GetScrollbar("defil");
        if (barre != null) barre.CurrentYPosition = (float)(premiereLigne * Case);
        Remplir();
    }

    private void Remplir()
    {
        for (int i = 0; i < Colonnes * Lignes; i++)
        {
            int index = premiereLigne * Colonnes + i;
            bool present = index < filtre.Count;
            vue[i].Itemstack = present ? filtre[index].pile : null;
            cles[i] = present ? filtre[index].entree.Cle : null;
        }
        var pins = tout.Where(x => epingles.Contains(x.entree.Cle)).Take(Colonnes).ToList();
        Composer?.GetDynamicText("aide-epingles")?.SetNewText(pins.Count == 0 ? Lang.Get("curveostockage:aide-epingles-vide") : "");
        for (int i = 0; i < Colonnes; i++)
        {
            vueEpingles[i].Itemstack = i < pins.Count ? pins[i].pile : null;
            clesEpingles[i] = i < pins.Count ? pins[i].entree.Cle : null;
        }
    }

    /// <summary>Clic sur une case : prendre (clic, clic droit = moitié, maj = vers l'inventaire), épingler (molette) ou déposer ce qu'on tient.</summary>
    public void Clic(bool pins, int slotId, ItemStackMoveOperation op)
    {
        if (op.MouseButton == EnumMouseButton.Wheel) { if (!pins) Defiler(op.WheelDir > 0 ? -1 : 1); return; }
        var tableau = pins ? clesEpingles : cles;
        string? cle = slotId >= 0 && slotId < tableau.Length ? tableau[slotId] : null;
        if (op.MouseButton == EnumMouseButton.Middle)
        {
            if (cle != null) envoyer(IdPaquets.Epingler, SerializerUtil.Serialize(cle));
            return;
        }
        var souris = capi.World.Player.InventoryManager.MouseItemSlot;
        if (souris != null && !souris.Empty) { envoyer(IdPaquets.DeposerSouris, null); return; }
        if (cle == null) return;
        bool maj = op.ShiftDown || capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] || capi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];
        int mode = maj ? 2 : op.MouseButton == EnumMouseButton.Right ? 1 : 0;
        envoyer(IdPaquets.Extraire, SerializerUtil.Serialize(new PaquetExtraire { Cle = cle, Mode = mode }));
    }
}

/// <summary>Le jeu retient la position des fenêtres déplacées ; les nôtres reprennent leur place (centre, bord droit) à chaque ouverture.</summary>
public static class Positions
{
    public static void Oublier(ICoreClientAPI capi, string nom)
    {
        if (capi.Gui.GetDialogPosition(nom) != null) capi.Gui.SetDialogPosition(nom, null);
    }
}

/// <summary>Terminal posé : boîte de dialogue liée au bloc.</summary>
public class GuiTerminal : GuiDialogBlockEntity
{
    private readonly PanneauStockage? panneau;
    private EnumPosFlag position;

    public GuiTerminal(ICoreClientAPI capi, BETerminal terminal)
        : base(Lang.Get("curveostockage:terminal-titre"), terminal.Inventory, terminal.Pos, capi)
    {
        if (IsDuplicate) return;
        panneau = new PanneauStockage(capi, (id, data) => capi.Network.SendBlockEntityPacket(BlockEntityPosition, id, data));
        Positions.Oublier(capi, "curveostockage-terminal" + BlockEntityPosition);
        position = GetFreePos("smallblockgui");
        var dialogue = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);
        SingleComposer = panneau.Composer_("curveostockage-terminal" + BlockEntityPosition, DialogTitle, dialogue, () => TryClose(), Inventory, DoSendPacket,
            c => SingleComposer = c);
    }

    public void Recevoir(PaquetListe paquet) => panneau?.Recevoir(paquet);

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        if (capi.Gui.GetDialogPosition(SingleComposer.DialogName) == null) OccupyPos("smallblockgui", position);
    }

    public override void OnGuiClosed()
    {
        panneau?.Fermer();
        base.OnGuiClosed();
        FreePos("smallblockgui", position);
    }
}
