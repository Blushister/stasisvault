using Vintagestory.API.Config;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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
