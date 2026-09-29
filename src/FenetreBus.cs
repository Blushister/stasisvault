using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace CurveoStockage;

/// <summary>Fenêtre commune aux trois bus : bloc visé et état, case d'entrée (import), 9 cases de filtre, réglages.</summary>
public class GuiBus : FenetreBloc
{
    private const double L = 456;
    private readonly BEBus bus;

    public GuiBus(ICoreClientAPI capi, BEBus bus) : base(bus.Titre, bus.Inventory, bus.Pos, capi)
    {
        this.bus = bus;
        if (IsDuplicate) return;
        var bornes = new List<ElementBounds>();
        var ajouts = new List<Action<GuiComposer>>();
        double y = 38;

        // Bloc visé et état
        var cibleEtB = ElementBounds.Fixed(0, y, L, 16);
        var carteB = ElementBounds.Fixed(0, y + 22, L, 58);
        var nomB = ElementBounds.Fixed(14, y + 22 + 8, L - 150, 22);
        var etatB = ElementBounds.Fixed(14, y + 22 + 32, L - 28, 20);
        var badgeB = ElementBounds.Fixed(L - 134, y + 22 + 6, 122, 26);
        bornes.AddRange(new[] { cibleEtB, carteB, nomB, etatB, badgeB });
        ajouts.Add(c =>
        {
            c.AddStaticText(Lang.Get("curveostockage:section-cible").ToUpperInvariant(), Etiquette(), cibleEtB);
            c.AddStaticElement(new SurfaceStasis(capi, carteB, Style.Surface, Style.Trait));
            c.AddDynamicText("", Style.Police(15, Style.Texte, gras: true), nomB, "cible");
            c.AddDynamicText("", Style.Police(13, Style.Texte3), etatB, "etat");
            c.AddInteractiveElement(new BadgeStasis(capi, badgeB), "badge");
        });
        y += 22 + 58 + 16;

        // Case d'entrée : tout ce qu'on y pose (main, maj+clic, trémie, goulotte) part dans le réseau
        if (bus.NbTampon > 0)
        {
            var entreeEtB = ElementBounds.Fixed(0, y, L, 16);
            var entreeB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y + 22, 1, 1);
            var entreeNoteB = ElementBounds.Fixed(Case + 10, y + 22 + 4, L - Case - 10, 44);
            bornes.AddRange(new[] { entreeEtB, entreeB, entreeNoteB });
            ajouts.Add(c =>
            {
                c.AddStaticText(Lang.Get("curveostockage:section-entree").ToUpperInvariant(), Etiquette(), entreeEtB);
                c.AddInteractiveElement(new GrilleStasis(capi, Inventory, DoSendPacket, 1, new[] { 0 }, entreeB, cible: true), "entree");
                c.AddStaticText(Lang.Get("curveostockage:note-entree"), Style.Police(13.5, Style.Texte3), entreeNoteB);
            });
            y += 22 + Case + 14;
        }

        // Filtre
        var filtreEtB = ElementBounds.Fixed(0, y, L, 16);
        var filtreB = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, y + 22, BEBus.NbFiltres, 1);
        bornes.AddRange(new[] { filtreEtB, filtreB });
        var cases = Enumerable.Range(bus.NbTampon, BEBus.NbFiltres).ToArray();
        ajouts.Add(c =>
        {
            c.AddStaticText(Lang.Get("curveostockage:section-filtre").ToUpperInvariant(), Etiquette(), filtreEtB);
            c.AddInteractiveElement(new GrilleStasis(capi, Inventory, DoSendPacket, BEBus.NbFiltres, cases, filtreB), "filtre");
        });
        y += 22 + Case + 8;
        if (!bus.FiltreAvecQuantite)
        {
            var modeB = ElementBounds.Fixed(0, y, 300, 30);
            bornes.Add(modeB);
            ajouts.Add(c => c.AddInteractiveElement(new SegmentsStasis(capi, modeB,
                new[] { Lang.Get("curveostockage:liste-blanche"), Lang.Get("curveostockage:liste-noire") }, bus.ListeNoire ? 1 : 0,
                i => Envoyer(BEBus.PaquetListeNoire, i == 1)), "mode"));
            y += 38;
        }
        var noteB = ElementBounds.Fixed(0, y, L, 40);
        bornes.Add(noteB);
        string note = bus switch
        {
            BEBusExport => "curveostockage:note-export",
            BEBusStockage => "curveostockage:note-stockage",
            _ => "curveostockage:note-import",
        };
        ajouts.Add(c => c.AddStaticText(Lang.Get(note), Style.Police(13.5, Style.Texte3), noteB));
        y += 46;

        // Accès du réseau au rangement (bus de stockage)
        if (bus is BEBusStockage stockage)
        {
            var accesEtB = ElementBounds.Fixed(0, y, L, 16);
            var accesB = ElementBounds.Fixed(0, y + 22, 300, 30);
            bornes.AddRange(new[] { accesEtB, accesB });
            ajouts.Add(c =>
            {
                c.AddStaticText(Lang.Get("curveostockage:section-acces").ToUpperInvariant(), Etiquette(), accesEtB);
                c.AddInteractiveElement(new SegmentsStasis(capi, accesB,
                    new[] { Lang.Get("curveostockage:acces-tout"), Lang.Get("curveostockage:acces-retrait") }, stockage.RetraitSeul ? 1 : 0,
                    i => Envoyer(BEBus.PaquetRetraitSeul, i == 1)), "acces");
            });
            y += 22 + 30 + 14;
        }

        var aideB = ElementBounds.Fixed(0, y, L, 48);
        bornes.Add(aideB);
        var aides = bus.FiltreAvecQuantite
            ? new[] { (Lang.Get("curveostockage:touche-clic"), Lang.Get("curveostockage:aide-filtre-pile")),
                      (Lang.Get("curveostockage:touche-clic-droit"), Lang.Get("curveostockage:aide-filtre-un")),
                      (Lang.Get("curveostockage:touche-main-vide"), Lang.Get("curveostockage:aide-filtre-vider")) }
            : new[] { (Lang.Get("curveostockage:touche-clic"), Lang.Get("curveostockage:aide-filtre-ajouter")),
                      (Lang.Get("curveostockage:touche-main-vide"), Lang.Get("curveostockage:aide-filtre-vider")) };
        ajouts.Add(c => c.AddInteractiveElement(new AideTouches(capi, aideB, aides), "aide"));

        var composer = Commencer("curveostockage-bus", Fond(bornes));
        foreach (var a in ajouts) a(composer);
        composer.EndChildElements();
        SingleComposer = composer.Compose();
        Maj();
    }

    private void Envoyer(int paquet, bool valeur)
        => capi.Network.SendBlockEntityPacket(BlockEntityPosition, paquet, new[] { (byte)(valeur ? 1 : 0) });

    public void Maj()
    {
        if (SingleComposer == null) return;
        var pos = bus.PosCible;
        var bloc = capi.World.BlockAccessor.GetBlock(pos);
        SingleComposer.GetDynamicText("cible")?.SetNewText(bloc.Id == 0 ? Lang.Get("curveostockage:bus-rien") : bloc.GetPlacedBlockName(capi.World, pos));
        SingleComposer.GetDynamicText("etat")?.SetNewText(bus.Etat.Length > 0 ? Lang.Get(bus.Etat) : "");
        (SingleComposer.GetElement("badge") as BadgeStasis)?.Definir(
            Lang.Get(bus.Etat.Length == 0 ? "curveostockage:badge-bus-actif" : "curveostockage:badge-bus-arret"),
            bus.Etat.Length == 0 ? BadgeStasis.Ton.Actif : BadgeStasis.Ton.Alerte);
        if (SingleComposer.GetElement("mode") is SegmentsStasis mode) mode.Actif = bus.ListeNoire ? 1 : 0;
        if (bus is BEBusStockage s && SingleComposer.GetElement("acces") is SegmentsStasis acces) acces.Actif = s.RetraitSeul ? 1 : 0;
    }
}
