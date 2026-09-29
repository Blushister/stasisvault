using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CurveoStockage;

public class StockageSystem : ModSystem
{
    /// <summary>Augmente à chaque pose ou retrait d'un bloc de réseau : les cœurs recalculent alors leur réseau.</summary>
    public static int Revision;
    public static ConfigStockage Config = new();
    public const string Canal = "curveostockage";
    public RegistreCylindres? Registre;
    public GestionAncres? Ancres;
    public GestionEpingles? Epingles;
    public GestionAtelier? Atelier;
    public ServeurTablettes? TablettesServeur;
    public ClientTablettes? Tablettes;
    /// <summary>Bras de conduit déjà tesselés pour les bus (par bloc et direction), valables pour cette session.</summary>
    public readonly Dictionary<string, MeshData> BrasBus = new();

    public static StockageSystem De(ICoreAPI api) => api.ModLoader.GetModSystem<StockageSystem>();

    /// <summary>
    /// stasisvault.json ; à défaut, l'ancien curveostockage.json (avant la 1.9) est converti puis renommé en .bak.
    /// </summary>
    private static ConfigStockage ChargerConfig(ICoreServerAPI sapi)
    {
        var lue = sapi.LoadModConfig<ConfigStockage>(ConfigStockage.Fichier);
        if (lue != null) return lue;
        var ancien = sapi.LoadModConfig<Newtonsoft.Json.Linq.JObject>(ConfigStockage.AncienFichier);
        if (ancien == null) return new ConfigStockage();
        var convertie = ConfigStockage.Lire(ancien);
        try
        {
            var chemin = System.IO.Path.Combine(Vintagestory.API.Config.GamePaths.ModConfig, ConfigStockage.AncienFichier);
            System.IO.File.Move(chemin, chemin + ".bak", true);
            sapi.Logger.Notification("[curveostockage] config convertie : " + ConfigStockage.AncienFichier + " → " + ConfigStockage.Fichier);
        }
        catch (Exception e) { sapi.Logger.Warning("[curveostockage] ancienne config non renommée : " + e.Message); }
        return convertie;
    }

    /// <summary>Retire les recettes des cylindres désactivés dans la config ; renvoie le nombre de recettes retirées.</summary>
    public static int RetirerRecettes(IWorldAccessor world)
    {
        var codes = Config.RecettesDesactivees();
        return codes.Count == 0 ? 0 : world.GridRecipes.RemoveAll(r => codes.Contains(PaquetRemplir.CodeSortie(r)));
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("curveostockage.BlockReseau", typeof(BlockReseau));
        api.RegisterBlockClass("curveostockage.BlockConduit", typeof(BlockConduit));
        api.RegisterBlockEntityClass("curveostockage.Coeur", typeof(BECoeur));
        api.RegisterBlockEntityClass("curveostockage.Baie", typeof(BEBaie));
        api.RegisterBlockEntityClass("curveostockage.Terminal", typeof(BETerminal));
        api.RegisterBlockEntityClass("curveostockage.Stabilisateur", typeof(BEStabilisateur));
        api.RegisterItemClass("curveostockage.ItemCylindre", typeof(ItemCylindre));
        api.RegisterItemClass("curveostockage.ItemTablette", typeof(ItemTablette));
        api.RegisterBlockEntityClass("curveostockage.Ancre", typeof(BEAncre));
        api.RegisterBlockClass("curveostockage.BlockBus", typeof(BlockBus));
        api.RegisterBlockClass("curveostockage.BlockAutomate", typeof(BlockAutomate));
        api.RegisterBlockEntityClass("curveostockage.Automate", typeof(BEAutomate));
        api.RegisterItemClass("curveostockage.ItemCarte", typeof(ItemCarte));
        api.RegisterBlockEntityClass("curveostockage.BusImport", typeof(BEBusImport));
        api.RegisterBlockEntityClass("curveostockage.BusExport", typeof(BEBusExport));
        api.RegisterBlockEntityClass("curveostockage.BusStockage", typeof(BEBusStockage));
        api.Network.RegisterChannel(Canal)
            .RegisterMessageType<MsgTablette>()
            .RegisterMessageType<PaquetListe>()
            .RegisterMessageType<MsgInfoTablette>()
            .RegisterMessageType<MsgSignalTablette>()
            .RegisterMessageType<MsgConfig>();
    }

    public override void StartClientSide(ICoreClientAPI capi)
    {
        // En solo, client et serveur partagent la même config ; en multijoueur, le serveur envoie la sienne
        capi.Network.GetChannel(Canal).SetMessageHandler<MsgConfig>(m =>
        {
            if (m.Lire() is not ConfigStockage c) return;
            Config = c;
            RetirerRecettes(capi.World);
        });
        Tablettes = new ClientTablettes(capi);
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        try { Config = ChargerConfig(sapi); }
        catch (Exception e) { sapi.Logger.Error("[curveostockage] config illisible, valeurs par défaut : " + e.Message); Config = new ConfigStockage(); }
        sapi.StoreModConfig(Config, ConfigStockage.Fichier);
        Registre = new RegistreCylindres(sapi);
        Ancres = new GestionAncres(sapi);
        Epingles = new GestionEpingles(sapi);
        Atelier = new GestionAtelier(sapi);
        TablettesServeur = new ServeurTablettes(sapi);
        if (Config.AutoTestActif) AutoTest.Enregistrer(sapi);
        sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            int n = RetirerRecettes(sapi.World);
            if (n > 0) sapi.Logger.Notification($"[curveostockage] {n} recette(s) retirée(s) : parties du mod désactivées dans la config");
        });
        var canal = sapi.Network.GetChannel(Canal);
        sapi.Event.PlayerNowPlaying += joueur => canal.SendPacket(MsgConfig.De(Config), joueur);
    }
}
