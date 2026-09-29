using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CurveoStockage;

/// <summary>
/// Terminal de stockage. Son inventaire réel n'a qu'un emplacement « dépôt » : tout ce qu'on y pose
/// (y compris par maj+clic depuis son inventaire) part aussitôt dans le réseau. La grille du contenu
/// est virtuelle, côté client ; chaque clic y devient une demande au serveur.
/// </summary>
public class BETerminal : BlockEntityOpenableContainer
{
    private readonly InventoryGeneric depot;
    public override InventoryBase Inventory => depot;
    public override string InventoryClassName => "curveostockage-terminal";

    private readonly HashSet<string> spectateurs = new();
    public bool AUnSpectateur => spectateurs.Count > 0;
    private bool absorbe;
    private string? acteur;

    public BETerminal()
    {
        depot = new InventoryGeneric(1, null, null);
        // Comme les coffres du jeu (qui ont BaseWeight 1 + un bonus), mais plus fort : un maj+clic depuis
        // l'inventaire du joueur doit viser le terminal plutôt que le sac à dos.
        depot.BaseWeight = 10f;
        depot.OnGetSuitability = (source, cible, fusion) => 10f + (fusion ? 3f : 1f) + (source.Inventory is InventoryBasePlayer ? 5f : 0f);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) depot.SlotModified += _ => Absorber();
    }

    private BECoeur? Coeur(out string? erreur) => Reseau.TrouverCoeur(Api.World, Pos, out erreur);

    private void Absorber()
    {
        var slot = depot[0];
        if (absorbe || slot.Empty) return;
        absorbe = true;
        try
        {
            var coeur = Coeur(out _);
            if (coeur == null) return;
            coeur.Inserer(slot.Itemstack, out _, acteur);
            if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            slot.MarkDirty();
        }
        finally { absorbe = false; }
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            toggleInventoryDialogClient(byPlayer, () => new GuiTerminal((ICoreClientAPI)Api, this));
        return true;
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        acteur = player.PlayerName;
        base.OnReceivedClientPacket(player, packetid, data);
#pragma warning disable CS0618 // change en 1.23 (voir Stabilisateur.Autorise)
        bool autorise = new CachedAccessPerms(Api.World, Pos, player).IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, validatePickRange: true, "terminal de stockage");
#pragma warning restore CS0618
        switch (packetid)
        {
            case 1000:
                if (!autorise) return;
                spectateurs.Add(player.PlayerUID);
                EnvoyerA((IServerPlayer)player);
                return;
            case 1001:
                spectateurs.Remove(player.PlayerUID);
                StockageSystem.De(Api).Atelier?.Desactiver(player.PlayerUID);
                return;
            case IdPaquets.ViderAtelier:
                StockageSystem.De(Api).Atelier?.Vider(autorise ? Coeur(out _) : null, (IServerPlayer)player, data?.Length > 0 && data[0] == 1);
                if (autorise) EnvoyerA((IServerPlayer)player);
                return;
        }
        if (packetid < IdPaquets.DemandeListe || packetid >= IdPaquets.Liste || !autorise) return;

        var sp = (IServerPlayer)player;
        var coeur = Coeur(out var erreur);
        if (coeur == null) { Refuser(sp, erreur ?? "curveostockage:erreur-sans-coeur"); return; }
        switch (packetid)
        {
            case IdPaquets.DemandeListe:
                EnvoyerA(sp);
                break;
            case IdPaquets.DeposerSouris:
                if (Operations.DeposerSouris(coeur, sp) is string refus) Refuser(sp, refus);
                break;
            case IdPaquets.Extraire:
                Operations.Extraire(coeur, sp, PaquetExtraire.Lire(data));
                break;
            case IdPaquets.Epingler:
                Operations.Epingler(Api, sp, data);
                EnvoyerA(sp);
                break;
            case IdPaquets.Perforer:
                if (PaquetRemplir.Lire(data) is PaquetRemplir pp) Fabrication.Informer(sp, Fabrication.Perforer(coeur, sp, pp), null, t => Refuser(sp, t));
                break;
            case IdPaquets.Commander:
                if (PaquetCommande.Lire(data) is PaquetCommande pc)
                {
                    var refusCmd = Fabrication.Commander(coeur, sp, pc, out var detail);
                    Fabrication.Informer(sp, refusCmd ?? "curveostockage:commande-lancee", detail, t => Refuser(sp, t));
                    EnvoyerA(sp);
                }
                break;
            case IdPaquets.RemplirAtelier:
                if (PaquetRemplir.Lire(data) is PaquetRemplir pr && StockageSystem.De(Api).Atelier?.RemplirRecette(coeur, sp, pr) is string msg) Refuser(sp, msg);
                break;
        }
    }

    public void EnvoyerListe(PaquetListe liste)
    {
        foreach (var uid in spectateurs.ToArray())
        {
            if (Api.World.PlayerByUid(uid) is not IServerPlayer sp || sp.ConnectionState != EnumClientState.Playing)
            {
                spectateurs.Remove(uid);
                continue;
            }
            liste.Epingles = StockageSystem.De(Api).Epingles?.De(uid) ?? new();
            ActiverAtelier(sp, liste);
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(sp, Pos, IdPaquets.Liste, SerializerUtil.Serialize(liste));
        }
    }

    private void EnvoyerA(IServerPlayer sp)
    {
        var coeur = Coeur(out var erreur);
        var liste = coeur?.Lister() ?? new PaquetListe { Erreur = erreur };
        liste.Epingles = StockageSystem.De(Api).Epingles?.De(sp.PlayerUID) ?? new();
        ActiverAtelier(sp, liste);
        ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(sp, Pos, IdPaquets.Liste, SerializerUtil.Serialize(liste));
    }

    private void ActiverAtelier(IServerPlayer sp, PaquetListe liste)
    {
        var atelier = StockageSystem.De(Api).Atelier;
        var coeur = liste.Atelier ? Coeur(out _) : null;
        if (coeur != null) atelier?.Activer(sp, coeur.Pos);
        else atelier?.Desactiver(sp.PlayerUID);
    }

    private void Refuser(IServerPlayer sp, string cle)
        => ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(sp, Pos, IdPaquets.Refus, SerializerUtil.Serialize(cle));

    /// <summary>Pas de « vitesse de dégradation » de coffre : la case de dépôt ne garde rien, tout part dans le réseau.</summary>
    public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc) { }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);
        if (packetid == IdPaquets.Liste)
            (invDialog as GuiTerminal)?.Recevoir(SerializerUtil.Deserialize<PaquetListe>(data));
        else if (packetid == IdPaquets.Refus)
            ((ICoreClientAPI)Api).TriggerIngameError(this, "curveostockage", Lang.Get(SerializerUtil.Deserialize<string>(data)));
    }
}
