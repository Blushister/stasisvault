using Vintagestory.API.Config;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>
/// /stockage autotest : monte un petit réseau au-dessus du spawn, range des objets, fait passer 10 jours
/// avec un stabilisateur en stase à moitié chargé, et vérifie la fraîcheur. Réservé aux admins,
/// à lancer sur un monde de test (le temps de la partie avance de 10 jours).
/// </summary>
public static class AutoTest
{
    public static void Enregistrer(ICoreServerAPI sapi)
    {
        sapi.ChatCommands.Create("stockage")
            .WithDescription("Stockage temporel (admin)")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("autotest")
                .WithDescription("Test automatique sur un monde de test (fait avancer le temps de 10 jours)")
                .HandleWith(_ =>
                {
                    var p = sapi.World.DefaultSpawnPosition.AsBlockPos;
                    int cs = GlobalConstants.ChunkSize;
                    sapi.WorldManager.LoadChunkColumnPriority(p.X / cs, p.Z / cs, new ChunkLoadOptions
                    {
                        OnLoaded = () => sapi.Event.EnqueueMainThreadTask(() =>
                        {
                            try { Lancer(sapi); }
                            catch (Exception e) { sapi.Logger.Error("Autotest stockage : ÉCHEC " + e); }
                        }, "autotest-stockage")
                    });
                    return TextCommandResult.Success("Autotest lancé (résultat dans le journal).");
                })
            .EndSubCommand();
    }

    private static string Lancer(ICoreServerAPI sapi)
    {
        var r = new StringBuilder("Autotest stockage :");
        var w = sapi.World;
        var ba = w.BlockAccessor;
        var p0 = w.DefaultSpawnPosition.AsBlockPos;
        var p = new BlockPos(p0.X, ba.GetTerrainMapheightAt(p0) + 3, p0.Z);
        r.Append($"\n- position {p}, chunk chargé : {ba.GetChunkAtBlockPos(p) != null}");
        Block B(string code) => w.GetBlock(new AssetLocation("curveostockage:" + code)) ?? throw new Exception("bloc introuvable " + code);
        void Poser(string code, BlockPos pos)
        {
            var bloc = B(code);
            ba.SetBlock(bloc.Id, pos);
            if (bloc.EntityClass != null && ba.GetBlockEntity(pos) == null) ba.SpawnBlockEntity(bloc.EntityClass, pos);
            StockageSystem.Revision++;
        }
        var pCoeur = p; var pBaie = p.EastCopy(); var pStab = p.WestCopy(); var pTerm = p.NorthCopy(); var pCond = p.SouthCopy();
        Poser("coeur-north", pCoeur); Poser("baie-vide-north", pBaie); Poser("stabilisateur-north", pStab);
        Poser("terminal-north", pTerm); Poser("conduit-aucune", pCond);
        (B("conduit-aucune") as BlockConduit)?.Raccorder(w, pCond);
        r.Append($"\n- conduit raccordé : {ba.GetBlock(pCond).Code.Path}");

        var coeur = ba.GetBlockEntity(pCoeur) as BECoeur ?? throw new Exception("pas de BE cœur");
        var baie = ba.GetBlockEntity(pBaie) as BEBaie ?? throw new Exception("pas de BE baie");
        var stab = ba.GetBlockEntity(pStab) as BEStabilisateur ?? throw new Exception("pas de BE stabilisateur");
        ItemStack Objet(string code, int n = 1) => new(w.GetItem(new AssetLocation(code)) ?? throw new Exception("objet introuvable " + code), n);
        baie.Inventory[0].Itemstack = Objet("curveostockage:cylindre-cuivre"); baie.Inventory[0].MarkDirty();
        baie.Inventory[1].Itemstack = Objet("curveostockage:cylindre-stase"); baie.Inventory[1].MarkDirty();
        coeur.MiseAJour();
        r.Append($"\n- apparence de la baie : {ba.GetBlock(pBaie).Code.Path}");

        stab.Carburant = 5; stab.Mode = 3;
        coeur.MiseAJour();

        string Inserer(string code, int n) { var pile = Objet(code, n); int ok = coeur.Inserer(pile, out var refus, "autotest"); return $"{code.Split(':')[1]} {ok}/{n} ({refus ?? "ok"})"; }
        r.Append("\n- rangé : " + string.Join(", ", new[] {
            Inserer("game:flaxfibers", 64), Inserer("game:vegetable-onion", 10), Inserer("game:vegetable-cassava", 10), Inserer("game:redmeat-raw", 20) }));
        var liste = coeur.Lister();
        r.Append($"\n- réseau : {liste.Objets}/{liste.ObjetsMax} objets, {liste.Types}/{liste.TypesMax} types");
        coeur.MiseAJour();
        var top = (baie.Inventory[0].Itemstack!.Attributes["cylTopCodes"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
        r.Append($"\n- journal : {coeur.Journal.Count} évènements ({string.Join(", ", coeur.Journal.Select(e => e.Type + ":" + e.Code.Split(':').Last() + "×" + e.Quantite))})");
        r.Append($"\n- aperçu du cylindre cuivre : {string.Join(", ", top)}");
        string? Cle(string chemin) => coeur.Lister().Entrees.Select(e => e.Cle).FirstOrDefault(c => coeur.Decrire(c).exemple?.Collectible.Code.Path == chemin);
        var reg = StockageSystem.De(sapi).Registre!;
        string OuEst(string? cle) => string.Join(", ", baie.Inventory.Where(s => !s.Empty)
            .Select(s => (m: s.Itemstack!.Collectible.Variant["materiau"], c: reg.Obtenir(s.Itemstack!.Attributes.GetString("cylId"), s.Itemstack!)))
            .Where(x => cle != null && x.c.Trouver(cle) != null).Select(x => x.m));
        r.Append($"\n- oignons rangés dans : {OuEst(Cle("vegetable-onion"))} (attendu : stase) ; fibres dans : {OuEst(Cle("flaxfibers"))} (attendu : cuivre)");

        double avant = w.Calendar.TotalHours;
        w.Calendar.Add(24 * 10);
        coeur.MiseAJour();
        r.Append($"\n- {w.Calendar.TotalHours - avant:0} h écoulées ; carburant restant {stab.Carburant:0.##} (attendu 0 : 5 engrenages = 5 jours de stase)");

        var noms = coeur.Lister().Entrees.Select(e => { var d = coeur.Decrire(e.Cle); return $"{d.exemple?.Collectible.Code.Path} ×{d.quantite}"; });
        r.Append("\n- contenu après 10 jours : " + string.Join(", ", noms));

        var oignons = Cle("vegetable-onion") is string co ? coeur.Extraire(co, 10) : null;
        if (oignons == null) r.Append("\n- ÉCHEC : oignons introuvables");
        else
        {
            var etat = oignons.Attributes["transitionstate"] as ITreeAttribute;
            var fait = (etat?["transitionedHours"] as FloatArrayAttribute)?.value;
            r.Append($"\n- oignons retirés ×{oignons.StackSize} : {fait?.FirstOrDefault():0.#} h de pourrissement (attendu ≈ 60 : 5 jours figés + 5 réfrigérés à ×0,5 ; sans stase ≈ 240)");
            coeur.Restituer(oignons);
        }
        r.Append("\n  (manioc : le séchage n'est pas figé, il doit être devenu du manioc séché ; viande : pourriture attendue après 5 jours)");

        // Tri automatique : de la viande rangée dans le cuivre (stabilisateur arrêté) doit rejoindre la stase,
        // des fibres rangées dans la stase doivent en sortir
        stab.Mode = 0;
        var cylCuivre = reg.Obtenir(baie.Inventory[0].Itemstack!.Attributes.GetString("cylId"), baie.Inventory[0].Itemstack!);
        var cylStase = reg.Obtenir(baie.Inventory[1].Itemstack!.Attributes.GetString("cylId"), baie.Inventory[1].Itemstack!);
        var viande = Objet("game:bushmeat-raw", 6);
        Transitions.Avancer(w, viande, 1f);
        string cleViande = Transitions.Cle(viande);
        cylCuivre.Ajouter(viande, cleViande, w);
        var fibres = Objet("game:cattailtops", 12);
        string cleFibres = Transitions.Cle(fibres);
        cylStase.Ajouter(fibres, cleFibres, w);
        r.Append($"\n- avant tri : viande dans {OuEst(cleViande)}, massettes dans {OuEst(cleFibres)}");
        coeur.MiseAJour();
        r.Append($"\n- après tri : viande dans {OuEst(cleViande)} (attendu : stase), massettes dans {OuEst(cleFibres)} (attendu : cuivre)");
        var viandeRangee = Objet("game:bushmeat-raw", 3);
        coeur.Inserer(viandeRangee, out _);
        r.Append($"\n- nouvelle viande, stabilisateur arrêté : rangée dans {OuEst(Transitions.Cle(viandeRangee))} (attendu : stase)");

        // Sauvegarde / rechargement d'un cylindre
        var reg2 = StockageSystem.De(sapi).Registre!;
        var cyl = reg2.Obtenir(baie.Inventory[0].Itemstack!.Attributes.GetString("cylId"), baie.Inventory[0].Itemstack!);
        var copie = Cylindre.DepuisOctets(cyl.VersOctets(), w);
        r.Append($"\n- sérialisation : {cyl.Types} types/{cyl.Objets} objets → relu {copie.Types}/{copie.Objets}");

        // Ancre temporelle : garde la colonne, puis la relâche sans toucher au spawn que le jeu garde déjà
        var pAncre = pTerm.NorthCopy(); var pEmet = pCoeur.UpCopy();
        Poser("ancre-north", pAncre); Poser("emetteur-north", pEmet);
        var ancre = ba.GetBlockEntity(pAncre) as BEAncre ?? throw new Exception("pas de BE ancre");
        var gestion = StockageSystem.De(sapi).Ancres!;
        long col = gestion.Colonne(pAncre);
        ancre.Carburant = 2; ancre.Tick();
        r.Append($"\n- ancre chargée : colonne retenue {gestion.EstRetenue(col)} (attendu True), forcée par nous {gestion.EstForceeParNous(col)}, déjà gardée par le jeu {gestion.EstForceeParLeJeu(col)}");
        ancre.Carburant = 0; ancre.Tick();
        r.Append($"\n- ancre vide : colonne retenue {gestion.EstRetenue(col)} (attendu False), toujours gardée par le jeu {gestion.EstForceeParLeJeu(col)} (attendu identique à avant)");
        var carte = Reseau.Explorer(w, pEmet, 1024);
        r.Append($"\n- réseau vu depuis l'émetteur : {carte.Coeurs.Count} cœur, {carte.Noeuds.Count} blocs");

        // Atelier : relié au réseau, il est annoncé aux terminaux
        bool avant_ = coeur.Lister().Atelier;
        var pAtelier = pBaie.EastCopy();
        Poser("atelier-north", pAtelier);
        coeur.MiseAJour();
        bool apres = coeur.Lister().Atelier;
        ba.SetBlock(0, pAtelier); StockageSystem.Revision++;
        r.Append($"\n- atelier : annoncé avant pose {avant_} (attendu False), après pose {apres} (attendu True), fabrication détectable {GestionAtelier.FabricationDetectable} (attendu True)");

        // Recettes : plan de remplissage de la grille pour la tablette (planches au joker, quartz, laiton, engrenage)
        var recTab = w.GridRecipes.FirstOrDefault(x => PaquetRemplir.CodeSortie(x) == "curveostockage:tablette");
        if (recTab == null) r.Append("\n- ÉCHEC : recette de la tablette introuvable");
        else
        {
            foreach (var (code, nb) in new[] { ("game:plank-oak", 10), ("game:clearquartz", 3), ("game:ingot-brass", 5), ("game:gear-rusty", 4) })
                coeur.Inserer(Objet(code, nb), out _);
            var (casesP, sourcesP, nP) = GestionAtelier.Planifier(coeur, recTab, true);
            int nIng = casesP.Count(x => x != null), nSrc = sourcesP.Count(x => x != null);
            r.Append($"\n- plan tablette : {nSrc}/{nIng} ingrédients trouvés dans le réseau (attendu {nIng}/{nIng}), fabrications max {nP} (attendu 2 : 5 laitons / 2)");
            var (_, _, n1) = GestionAtelier.Planifier(coeur, recTab, false);
            r.Append($"\n- plan tablette ×1 : {n1} (attendu 1) ; {w.GridRecipes.Count} recettes de grille chargées");
        }

        // Bus (v1.9) : import (coffre, trémie, liste noire), export (quantité à garder), stockage (lecture, retrait, liste blanche)
        BlockEntityContainer Coffre(BlockPos pos)
        {
            var bloc = w.GetBlock(new AssetLocation("game:chest-east")) ?? throw new Exception("coffre introuvable");
            ba.SetBlock(bloc.Id, pos);
            if (ba.GetBlockEntity(pos) == null) ba.SpawnBlockEntity(bloc.EntityClass, pos);
            return ba.GetBlockEntity(pos) as BlockEntityContainer ?? throw new Exception("pas de BE coffre");
        }
        void Remplir(BlockEntityContainer c, int i, string code, int n) { c.Inventory[i].Itemstack = Objet(code, n); c.Inventory[i].MarkDirty(); }
        long Qte(string code) => coeur.Contenu().Where(c => c.exemple.Collectible.Code.ToString() == code).Sum(c => c.quantite);
        long DansCoffre(BlockEntityContainer c, string code) => c.Inventory.Where(s => !s.Empty && s.Itemstack.Collectible.Code.ToString() == code).Sum(s => (long)s.StackSize);
        var pBusI = pCoeur.DownCopy(); var pCoffreI = pBusI.DownCopy();
        var pBusE = pCond.SouthCopy(); var pCoffreE = pBusE.SouthCopy();
        var pBusS = pCond.DownCopy(); var pCoffreS = pBusS.DownCopy();
        Poser("busimport-down", pBusI); Poser("busexport-south", pBusE); Poser("busstockage-down", pBusS);
        var coffreI = Coffre(pCoffreI); var coffreE = Coffre(pCoffreE); var coffreS = Coffre(pCoffreS);
        var busI = ba.GetBlockEntity(pBusI) as BEBusImport ?? throw new Exception("pas de BE bus d'import");
        var busE = ba.GetBlockEntity(pBusE) as BEBusExport ?? throw new Exception("pas de BE bus d'export");
        var busS = ba.GetBlockEntity(pBusS) as BEBusStockage ?? throw new Exception("pas de BE bus de stockage");
        coeur.MiseAJour();

        Remplir(coffreI, 0, "game:flint", 20);
        long silex0 = Qte("game:flint");
        busI.Tick();
        long apres1 = DansCoffre(coffreI, "game:flint"), reseau1 = Qte("game:flint") - silex0;
        busI.Tick();
        r.Append($"\n- bus d'import : coffre {apres1} silex après 1 transfert (attendu 4), réseau +{reseau1} (attendu 16), puis coffre {DansCoffre(coffreI, "game:flint")} (attendu 0)");
        var source = new DummySlot(Objet("game:flint", 5));
        var entree = busI.Inventory.GetAutoPushIntoSlot(BlockFacing.UP, source);
        int pousse = entree == null ? 0 : source.TryPutInto(w, entree, 5);
        entree?.MarkDirty();
        r.Append($"\n- trémie → bus d'import : {pousse} poussés (attendu 5), case d'entrée vide {busI.Inventory[0].Empty} (attendu True), réseau +{Qte("game:flint") - silex0} silex (attendu 25)");
        busI.Inventory[1].Itemstack = Objet("game:flint", 1);
        busI.ListeNoire = true;
        Remplir(coffreI, 0, "game:flint", 3); Remplir(coffreI, 1, "game:stick", 4);
        busI.Tick();
        bool trémieRefusee = busI.Inventory.GetAutoPushIntoSlot(BlockFacing.UP, new DummySlot(Objet("game:flint", 1))) == null;
        r.Append($"\n- liste noire (silex) : restent {DansCoffre(coffreI, "game:flint")} silex (attendu 3) et {DansCoffre(coffreI, "game:stick")} bâtons (attendu 0) ; silex refusé à la trémie {trémieRefusee} (attendu True)");

        busE.Inventory[0].Itemstack = Objet("game:flint", 10);
        busE.Tick(); long e1 = DansCoffre(coffreE, "game:flint");
        busE.Tick(); long e2 = DansCoffre(coffreE, "game:flint");
        r.Append($"\n- bus d'export (garder 10 silex) : coffre {e1} puis {e2} (attendus 10 et 10), réseau {Qte("game:flint")} (attendu 15)");

        Remplir(coffreS, 0, "game:stick", 7);
        coeur.MiseAJour();
        long batons = Qte("game:stick");
        var cleBaton = coeur.Contenu().First(c => c.exemple.Collectible.Code.Path == "stick").cle;
        var pris = coeur.Extraire(cleBaton, 9);
        r.Append($"\n- bus de stockage : le réseau voit {batons} bâtons (attendu 11), retrait de {pris?.StackSize} → coffre {DansCoffre(coffreS, "game:stick")} (attendu 0 : le coffre passe en premier)");
        busS.Inventory[0].Itemstack = Objet("game:stick", 1);
        if (pris != null) coeur.Inserer(pris, out _);
        long blanche = DansCoffre(coffreS, "game:stick");
        busS.RetraitSeul = true;
        coeur.Inserer(Objet("game:stick", 2), out _);
        r.Append($"\n- liste blanche (bâtons) : {blanche} rangés dans le coffre (attendu 9) ; retrait seul : coffre {DansCoffre(coffreS, "game:stick")} (attendu 9)");

        var cfg = StockageSystem.Config;
        cfg.Cylindres.Materiaux["stase"].Actif = false; cfg.Cylindres.Materiaux["refrigere"].Actif = false;
        double tStase = BECoeur.Taux("stase", 3, 1), tFrigo = BECoeur.Taux("refrigere", 0, 0);
        int attendues = w.GridRecipes.Count(x => PaquetRemplir.CodeSortie(x) is "curveostockage:cylindre-refrigere" or "curveostockage:cylindre-stase");
        int retirees = StockageSystem.RetirerRecettes(w);
        cfg.Cylindres.Materiaux["stase"].Actif = true; cfg.Cylindres.Materiaux["refrigere"].Actif = true;
        r.Append($"\n- config sans stase ni réfrigéré : vitesse du cylindre de stase en mode stase {tStase:0.##} (attendu {cfg.Stabilisateur.ModeStase.Facteur:0.##}), réfrigéré {tFrigo:0.##} (attendu 1), recettes retirées {retirees} (attendu {attendues}, variantes comprises)");
        foreach (var pos in new[] { pBusI, pCoffreI, pBusE, pCoffreE, pBusS, pCoffreS }) ba.SetBlock(0, pos);

        // Capacité réglable par la config (cylindres existants compris) et cylindre en fer
        var ancienne = cfg.Cylindres.Materiaux["cuivre"];
        cfg.Cylindres.Materiaux["cuivre"] = new ReglageCylindre(3000, 40);
        var cylC = reg.Obtenir(baie.Inventory[0].Itemstack!.Attributes.GetString("cylId"), baie.Inventory[0].Itemstack!);
        r.Append($"\n- config de capacité (cuivre 3000/40) : cylindre existant {cylC.ObjetsMax}/{cylC.TypesMax} (attendu 3000/40)");
        cfg.Cylindres.Materiaux["cuivre"] = ancienne;
        var fer = Objet("curveostockage:cylindre-fer");
        var (fo, ft) = cfg.Capacite(fer.Collectible);
        bool recetteFer = w.GridRecipes.Any(x => PaquetRemplir.CodeSortie(x) == "curveostockage:cylindre-fer");
        r.Append($"\n- cylindre en fer : {fo}/{ft} (attendu 16000/75), recette chargée {recetteFer} (attendu True)");

        // Automate horloger : maintien du stock (cartes vierges) et commande avec sous-fabrication (conduits → automate)
        var pAuto = pStab.WestCopy();
        Poser("automate-north", pAuto);
        var auto = ba.GetBlockEntity(pAuto) as BEAutomate ?? throw new Exception("pas de BE automate");
        coeur.MiseAJour();
        ItemStack Carte(string sortie)
        {
            var rec = w.GridRecipes.First(x => PaquetRemplir.CodeSortie(x) == sortie);
            var c = Objet("curveostockage:carte-perforee");
            ItemCarte.Graver(c, rec, w.GridRecipes.IndexOf(rec));
            return c;
        }
        var carteConduit = Carte("curveostockage:conduit-aucune");
        var carteVierge = Carte("curveostockage:carte-vierge");
        var carteAuto = Carte("curveostockage:automate-north");
        carteVierge.Attributes.SetInt("garder", 6);
        foreach (var (i, c) in new[] { (0, carteConduit), (1, carteVierge), (2, carteAuto) }) { auto.Inventory[i].Itemstack = c; auto.Inventory[i].MarkDirty(); }
        coeur.Inserer(Objet("game:paper-parchment", 2), out _);
        for (int k = 0; k < 4; k++) auto.Tick(5);
        r.Append($"\n- automate, garder 6 cartes vierges : réseau {Qte("curveostockage:carte-vierge")} cartes (attendu 8 : 2 fabrications de 4), parchemin {Qte("game:paper-parchment")} (attendu 0), cadence sans axe {auto.Cadence} (attendu 1)");
        var manquesAvant = Cartes.Manques(coeur, carteAuto, 1);
        coeur.Inserer(Objet("game:ingot-copper", 2), out _);
        var manquesApres = Cartes.Manques(coeur, carteAuto, 1);
        r.Append($"\n- simulation de commande d'un automate : sans cuivre il manque [{string.Join(", ", manquesAvant.Select(m => m.Value + " " + m.Key))}] (attendu : 2 lingots de cuivre, pour le conduit), avec : [{string.Join(", ", manquesApres.Keys)}] (attendu vide)");
        coeur.AjouterCommande(new Commande { Carte = ItemCarte.Ident(carteAuto), Restant = 1, Total = 1, Qui = "autotest", NomSortie = "automate" }, false);
        for (int k = 0; k < 6 && coeur.Commandes.Count > 0; k++) auto.Tick(5);
        r.Append($"\n- commande d'un automate : fabriqués {Qte("curveostockage:automate-north")} (attendu 1), conduits restants {Qte("curveostockage:conduit-aucune")} (attendu 7 : 8 fabriqués, 1 utilisé), file vide {coeur.Commandes.Count == 0} (attendu True), journal « fabriqué » {coeur.Journal.Any(e => e.Type == 4)} (attendu True)");
        var liste2 = coeur.Lister();
        r.Append($"\n- terminal : {liste2.Fabricables.Count} objets fabricables annoncés (attendu 3)");
        ba.SetBlock(0, pAuto); StockageSystem.Revision++;

        // Config : conversion d'une config d'avant la 1.9, désactivation de chaque partie
        var ancienneConfig = ConfigStockage.Lire(Newtonsoft.Json.Linq.JObject.Parse(
            "{\"FacteurRefrigere\": 0.3, \"HeuresParEngrenageI\": 100, \"EngrenagesMax\": 32, \"PorteeEmetteur\": 64, \"CylindreStaseActif\": false, \"Capacites\": {\"acier\": {\"Objets\": 50000, \"Types\": 150}}}"));
        r.Append($"\n- ancienne config convertie : réfrigéré {ancienneConfig.Cylindres.FacteurRefrigere} (attendu 0,3), mode I {ancienneConfig.Stabilisateur.ModeI.HeuresParCharge} h (attendu 100), charge max {ancienneConfig.Stabilisateur.ChargeMax}/{ancienneConfig.Ancre.ChargeMax} (attendu 32/32), portée {ancienneConfig.SansFil.Portee} (attendu 64), stase {ancienneConfig.Cylindres.Materiaux["stase"].Actif}/{ancienneConfig.Stabilisateur.ModeStase.Actif} (attendu False/False), acier {ancienneConfig.Cylindres.Materiaux["acier"].Objets}/{ancienneConfig.Cylindres.Materiaux["acier"].Types} (attendu 50000/150)");
        var toutCoupe = new ConfigStockage();
        foreach (var m in toutCoupe.Cylindres.Materiaux.Values) m.Actif = false;
        toutCoupe.Stabilisateur.Actif = toutCoupe.Ancre.Actif = toutCoupe.SansFil.Actif = toutCoupe.Atelier.Actif = toutCoupe.Automate.Actif = false;
        toutCoupe.Bus.Import.Actif = toutCoupe.Bus.Export.Actif = toutCoupe.Bus.Stockage.Actif = false;
        var codesCoupes = toutCoupe.RecettesDesactivees();
        int sansRecette = codesCoupes.Count(code => !w.GridRecipes.Any(x => PaquetRemplir.CodeSortie(x) == code) && code != "curveostockage:cylindre-stase" && code != "curveostockage:cylindre-refrigere");
        r.Append($"\n- tout désactivé : {codesCoupes.Count} objets sans recette (attendu 16), dont {sansRecette} codes qui ne correspondent à aucune recette (attendu 0)");

        // Paquets clients : illisibles ou hors limites rejetés, valides acceptés
        byte[] P(PaquetExtraire p) => Vintagestory.API.Util.SerializerUtil.Serialize(p);
        bool illisible = PaquetExtraire.Lire(new byte[] { 0xFF, 0x13, 0x02, 0x99 }) == null;
        bool modeFaux = PaquetExtraire.Lire(P(new PaquetExtraire { Cle = "abc", Mode = 7 })) == null;
        bool cleLongue = PaquetExtraire.Lire(P(new PaquetExtraire { Cle = new string('a', 500), Mode = 0 })) == null;
        bool valide = PaquetExtraire.Lire(P(new PaquetExtraire { Cle = "abc", Mode = 2 })) != null;
        var relue = Vintagestory.API.Util.SerializerUtil.Deserialize<EntreeListe>(Vintagestory.API.Util.SerializerUtil.Serialize(new EntreeListe { Cle = "x", Taux = 0f }));
        r.Append($"\n- vitesse figée transmise au terminal : {relue.Taux} (attendu 0)");
        r.Append($"\n- paquets : illisible rejeté {illisible}, mode 7 rejeté {modeFaux}, clé géante rejetée {cleLongue}, valide accepté {valide} (tous attendus True)");

        foreach (var pos in new[] { pCoeur, pBaie, pStab, pTerm, pCond, pAncre, pEmet }) ba.SetBlock(0, pos);
        StockageSystem.Revision++;
        sapi.Logger.Notification(r.ToString());
        return r.ToString();
    }
}
