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

    public static StockageSystem De(ICoreAPI api) => api.ModLoader.GetModSystem<StockageSystem>();

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
        capi.Network.GetChannel(Canal).SetMessageHandler<MsgConfig>(m => { if (m.Lire() is ConfigStockage c) Config = c; });
        Tablettes = new ClientTablettes(capi);
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        try { Config = sapi.LoadModConfig<ConfigStockage>("curveostockage.json") ?? new ConfigStockage(); }
        catch (Exception e) { sapi.Logger.Error("[curveostockage] config illisible, valeurs par défaut : " + e.Message); Config = new ConfigStockage(); }
        sapi.StoreModConfig(Config, "curveostockage.json");
        Registre = new RegistreCylindres(sapi);
        Ancres = new GestionAncres(sapi);
        Epingles = new GestionEpingles(sapi);
        Atelier = new GestionAtelier(sapi);
        TablettesServeur = new ServeurTablettes(sapi);
        if (Config.AutoTestActif) AutoTest.Enregistrer(sapi);
        var canal = sapi.Network.GetChannel(Canal);
        sapi.Event.PlayerNowPlaying += joueur => canal.SendPacket(MsgConfig.De(Config), joueur);
    }
}
